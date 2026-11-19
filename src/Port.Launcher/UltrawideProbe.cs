using System.Collections;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Port.Hooks;

// Acceptance fixture: render original levels/states at simulated output shapes.
// Poses are fixtures, not a claim of automated level traversal.
internal sealed class UltrawideProbe : GameComponent
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly Game game;
    private readonly string directory;
    private readonly Type camera;
    private KeyboardState keyboard;
    private object? component, level;
    private int frames, index, phase, draws, captures;
    private bool pose, done;
    private Matrix expectedCamera;
    private static readonly string[] Scenes = ["center", "pause", "resume", "upper", "left", "right", "dark",
        "scan", "scan-pause", "scan-resume", "zoom", "enigma", "sign", "sign-pause", "sign-resume",
        "32x9", "32x9-pause", "32x9-resume", "16x9", "fade",
        "scan-left", "scan-right", "scan-target-left", "scan-target-right", "scan-zoom-in", "scan-zoom-out",
        "zoom-pause", "zoom-resume", "zoom-move", "16x9-scan", "16x9-zoom",
        "32x9-scan", "32x9-scan-pause", "32x9-scan-resume", "32x9-zoom", "32x9-zoom-pause", "32x9-zoom-resume"];
    private string ContentScene => Scenes[phase].StartsWith("16x9-", StringComparison.Ordinal) ||
        Scenes[phase].StartsWith("32x9-", StringComparison.Ordinal) ? Scenes[phase][5..] : Scenes[phase];

    internal UltrawideProbe(Game game, string directory) : base(game)
    {
        this.game = game;
        game.IsFixedTimeStep = false;
        game.InactiveSleepTime = TimeSpan.Zero;
        GameAccess.Get<GraphicsDeviceManager>(game, "m_graphics").SynchronizeWithVerticalRetrace = false;
        this.directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(this.directory);
        camera = game.GetType().Assembly.GetType("Scale.GAME.GAME_Camera")!;
        DesktopInput.ReadKeyboard = () => keyboard;
        DesktopInput.ReadMouse = () => default;
        DesktopInput.IsActive = _ => true;
        OptionsSession.OutputSize = _ => new(3440, 1440);
        HelpOptions.CaptureFrame = Capture;
        UpdateOrder = int.MaxValue;
    }

    public override void Update(GameTime gameTime)
    {
        if (++frames > 15000) throw new InvalidOperationException("Ultrawide probe timed out.");
        string state = GameAccess.Get(game, "m_state")!.ToString()!;
        component = GameAccess.Get(game, "m_stateComponent");
        if (state == "START" && component is not null)
        {
            if (GameAccess.Get(component, "m_menuState")!.ToString() == "START" && frames > 500) keyboard = new(Keys.Enter);
            if (GameAccess.Get(component, "m_menuState")!.ToString() == "SELECTION")
            {
                keyboard = default;
                GameAccess.Set(game, "m_levelIndexContinue", -1);
                var method = game.GetType().GetMethod("InitState", Flags)!;
                method.Invoke(game, [Enum.Parse(method.GetParameters()[0].ParameterType, "GAME")]);
            }
            return;
        }
        if (state != "GAME" || component is null || GameAccess.Get(component, "m_level") is not object loaded) return;
        level = loaded;
        ((GameComponent)component).Enabled = false;
        GameAccess.Set(component, "m_timerIntro", 0);
        if (done) { game.Exit(); return; }
        if (pose) return;
        if (GameAccess.Get<int>(component, "m_levelIndex") != index)
        {
            Call(level, "Dispose");
            var particles = game.GetType().Assembly.GetType("LowLevel.CLASS_PARTICULE")!;
            particles.GetMethod("Ark_PartKillAllGenerator")!.Invoke(null, null);
            particles.GetMethod("Ark_PartKillAllBoulette")!.Invoke(null, null);
            GameAccess.Set(component, "m_levelIndex", index);
            Call(component, "InitLevel");
            level = GameAccess.Get(component, "m_level")!;
        }
        Pose();
        pose = true;
        draws = 0;
    }

    private void Pose()
    {
        if (index == 0 && phase == 0) LegacyGraphicsProbe.Run(game.GraphicsDevice);
        string scene = ContentScene;
        bool superwide = Scenes[phase].StartsWith("32x9", StringComparison.Ordinal);
        bool standard = Scenes[phase].StartsWith("16x9", StringComparison.Ordinal);
        OptionsSession.OutputSize = _ => superwide ? new(5120, 1440) : standard ? new(1920, 1080) : new(3440, 1440);
        // Simulate both the selected mode and a physical ultrawide monitor. In
        // particular, keep the monitor wide for the 1080p regression fixture.
        var options = HelpOptions.Session(game);
        var resolution = standard ? new Resolution(1920, 1080)
            : superwide ? new(5120, 1440) : new(3440, 1440);
        GameAccess.Set(options, "<Settings>k__BackingField", options.Settings with { Resolution = resolution });
        if (scene == "16x9") OptionsSession.OutputSize = _ => new(3440, 1440);
        if (GameAccess.Get(level!, "map") is object previousMap) Call(previousMap, "GAZM_KillGenBg");
        SetState("PLATFORM");
        GameAccess.Set(level!, "m_pause", false);
        Tint(GameAccess.Get(component!, "m_sprPause")!, Color.Transparent);
        GameAccess.Set(level!, "m_activateItLumiere", true);
        var spriteType = game.GetType().Assembly.GetType("LowLevel.CLASS_SPR")!;
        spriteType.GetField("m_sprAnmAutoplayStopAll")!.SetValue(null, false);
        spriteType.GetField("m_sprEftStopAll")!.SetValue(null, false);
        var fade = game.GetType().Assembly.GetType("LowLevel.CLASS_MOD_FADE")!.GetField("m_fadeCurrent")!.GetValue(null)!;
        GameAccess.Set(fade, "m_fadeInc", 0f);
        GameAccess.Set(fade, "m_fadeAlpha", scene == "fade" ? 255f : 0f);
        Tint(GameAccess.Get(fade, "m_sprFade")!, scene == "fade" ? Color.White : Color.Transparent);
        Tint(GameAccess.Get(level!, "m_sprQuadNoir")!, Color.Transparent);

        var player = GameAccess.Get(level!, "Player")!;
        var start = GameAccess.Get<Vector2>(level!, "start");
        float worldWidth = GameAccess.Get<int>(level!, "Width") * 88;
        var position = new Vector2(scene is "left" or "scan-left" || scene.StartsWith("sign", StringComparison.Ordinal) ? 100
            : scene is "right" or "scan-right" ? worldWidth - 100 : worldWidth / 2,
            scene == "upper" ? Math.Max(360, start.Y - 2000) : start.Y);
        player.GetType().GetProperty("Position")!.SetValue(player, position);
        camera.GetField("m_scale")!.SetValue(null, 1f);
        camera.GetField("m_zoomMode")!.SetValue(null, (short)0);
        camera.GetMethods().Single(method => method.Name == "GACM_UpdateCamera" && method.GetParameters().Length == 1).Invoke(null, [position]);

        if (scene == "dark")
        {
            GameAccess.Set(level!, "m_activateItLumiere", false);
            var light = GameAccess.Get(level!, "m_sprMaskLumiere")!;
            var screen = Vector2.Transform(position, (Matrix)camera.GetField("m_camera")!.GetValue(null)!);
            light.GetType().GetProperty("m_sprX")!.SetValue(light, screen.X);
            light.GetType().GetProperty("m_sprY")!.SetValue(light, screen.Y - 70);
            Tint(light, Color.White);
            foreach (var contour in GameAccess.Get<object[]>(level!, "m_sprMaskLumiereContour")) Tint(contour, Color.White);
        }
        if (scene.StartsWith("scan", StringComparison.Ordinal))
        {
            Call(level!, "StartScanMode");
            Tint(GameAccess.Get(level!, "m_sprQuadNoir")!, new Color(255, 255, 255, 150));
            if (scene.StartsWith("scan-target", StringComparison.Ordinal)) CheckScanTarget(scene.EndsWith("right", StringComparison.Ordinal));
            if (scene is "scan-zoom-in" or "scan-zoom-out")
            {
                var cursor = GameAccess.Get(level!, "m_sprCursor")!;
                GameAccess.Set(cursor, "m_sprCameraOn", true);
                GameAccess.Set(cursor, "m_sprX", position.X);
                GameAccess.Set(cursor, "m_sprY", position.Y);
                var substate = level!.GetType().GetField("m_stateScan", Flags)!;
                substate.SetValue(level, Enum.Parse(substate.FieldType, scene == "scan-zoom-in" ? "ZOOM_IN" : "ZOOM_OUT"));
                camera.GetField("m_scale")!.SetValue(null, 4f);
                camera.GetMethods().Single(method => method.Name == "GACM_UpdateCamera" && method.GetParameters().Length == 1).Invoke(null, [position]);
            }
        }
        if (scene.StartsWith("zoom", StringComparison.Ordinal) || scene == "enigma")
        {
            var maps = GameAccess.Get<IList>(level!, "zoomMaps");
            Require(maps.Count > 0, "level has no inspection map");
            var map = maps[0]!;
            Call(map, "GAZM_Start");
            GameAccess.Set(level!, "map", map);
            if (scene != "enigma")
            {
                SetState("ZOOM");
                if (scene == "zoom-move") CheckZoomMove(map);
                var mapPosition = GameAccess.Get<Vector2>(map, "m_posCoord");
                camera.GetMethods().Single(method => method.Name == "GACM_UpdateCamera" && method.GetParameters().Length == 3)
                    .Invoke(null, [mapPosition, mapPosition, false]);
            }
            else
            {
                var puzzle = GameAccess.Get(map, "m_enigma")!;
                puzzle.GetType().GetMethod("StartEnigma", [typeof(int)])!.Invoke(puzzle, [GameAccess.Get<int>(map, "m_param")]);
                GameAccess.Set(level!, "m_enigmaCurrent", puzzle);
                SetState("ENIGMA");
                GameAccess.Set(fade, "m_fadeInc", 0f);
                GameAccess.Set(fade, "m_fadeAlpha", 0f);
                Tint(GameAccess.Get(fade, "m_sprFade")!, Color.Transparent);
            }
        }
        if (scene == "pause" || scene.EndsWith("-pause", StringComparison.Ordinal))
        {
            GameAccess.Set(level!, "m_pause", true);
            Tint(GameAccess.Get(component!, "m_sprPause")!, Color.White);
        }
        if (scene.StartsWith("sign", StringComparison.Ordinal))
        {
            SetState("SIGN");
            var sign = GameAccess.Get<Array>(level!, "tiles").Cast<object>()
                .First(tile => tile?.GetType().Name == "GAME_Tile_Sign");
            string source = GameAccess.Get<string>(sign, "m_text");
            var text = GameAccess.Get(level!, "m_fntTextBox")!;
            Call(text, "Ark_FntSetText", source);
            TutorialHints.RefreshText(text, true);
            Require(GameAccess.Get<string>(text, "m_fntText") == TutorialHints.Translate(source, true), "keyboard sign wording");
            TutorialHints.RefreshText(text, false);
            Require(GameAccess.Get<string>(text, "m_fntText") == source, "controller sign restoration");
            TutorialHints.RefreshText(text, true);
        }
        expectedCamera = (Matrix)camera.GetField("m_camera")!.GetValue(null)!;
        Require((int)camera.GetField("m_scnWidth")!.GetValue(null)! == 1280 &&
            (int)camera.GetField("m_scnHeight")!.GetValue(null)! == 720, "simulation camera inherited output resolution");
    }

    private void Capture(Game _, Texture2D frame)
    {
        if (!pose) return;
        draws++;
        string scene = Scenes[phase];
        int expectedWidth = ContentScene == "enigma" || scene.StartsWith("16x9", StringComparison.Ordinal)
            ? 1280 : scene.StartsWith("32x9", StringComparison.Ordinal) ? 2560 : 1720;
        Require(frame.Width == expectedWidth && frame.Height == 720, $"{scene} canvas is {frame.Width} x {frame.Height}");
        Require((Matrix)camera.GetField("m_camera")!.GetValue(null)! == expectedCamera, "drawing changed simulation camera");
        Require(Ultrawide.GetWidth() == 1280 && Ultrawide.GetScanWidth() == 1280, "drawing or scan context leaked into Update");
        if (scene == "pause" || scene.EndsWith("-pause", StringComparison.Ordinal) || scene.EndsWith("resume", StringComparison.Ordinal))
        {
            Require(GameAccess.Get<int>(HelpOptions.Session(game), "transitionFrames") == 0,
                $"{scene} started an aspect-ratio crossfade");
            if (draws == 1)
            {
                using var first = File.Create(Path.Combine(directory, $"level-{index}-{scene}-first.png"));
                frame.SaveAsPng(first, frame.Width, frame.Height);
            }
        }
        if (draws < 16) return;
        if (scene == "32x9")
        {
            var pixels = new Color[frame.Width * frame.Height];
            frame.GetData(pixels);
            int hearts = 0;
            for (int y = 170; y < 335; y++)
            for (int x = 50; x < 135; x++)
            {
                var p = pixels[y * frame.Width + x];
                if (p.R > 150 && p.G < 100 && p.B > 40) hearts++;
            }
            Require(hearts > 500, "health HUD lost its left-edge margin at 32:9");
        }
        if (scene == "16x9")
        {
            Require(HelpOptions.Session(game).Destination().Width < game.GraphicsDevice.PresentationParameters.BackBufferWidth,
                "1080p on an ultrawide monitor must have side bars");
        }
        if (scene == "fade")
        {
            var pixels = new Color[frame.Width * frame.Height];
            frame.GetData(pixels);
            Require(pixels.All(color => color.R == 0 && color.G == 0 && color.B == 0), "fade left an uncovered edge");
        }
        using (var output = File.Create(Path.Combine(directory, $"level-{index}-{scene}.png"))) frame.SaveAsPng(output, frame.Width, frame.Height);
        Console.WriteLine($"ULTRAWIDE_CAPTURE level={index} scene={scene} width={frame.Width} camera=restored");
        captures++;
        pose = false;
        if (++phase == Scenes.Length) { phase = 0; if (++index == 7) done = true; }
    }

    private void SetState(string state) => GameAccess.Set(level!, "m_state", Enum.Parse(level!.GetType().GetField("m_state")!.FieldType, state));
    private static void Tint(object sprite, Color color)
    {
        GameAccess.Set(sprite, "m_sprRGBA", color);
        GameAccess.Set(sprite, "m_sprEftFadeOn", false);
    }
    private static void Call(object instance, string method, params object[] args) => instance.GetType().GetMethod(method, Flags)!.Invoke(instance, args);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Ultrawide probe: " + message);
    }
    internal void Verify()
    {
        Require(done && captures == 7 * Scenes.Length, "incomplete captures");
        Console.WriteLine($"ULTRAWIDE_PROBE_RESULT levels=7 captures={captures} camera=ok scan=ultrawide scan-targets=ok zoom=ultrawide zoom-movement=ok puzzle=16:9 pause-width=stable pause-crossfade=none resolution=ok masks=ok");
    }

    private Array PadButtons() => (Array)game.GetType().Assembly.GetType("LowLevel.CLASS_GAMEPAD")!
        .GetField("m_gpadButtons")!.GetValue(game.GetType().Assembly.GetType("LowLevel.CLASS_GAMEPAD")!.GetField("m_gpad")!.GetValue(null))!;

    private static void Button(Array buttons, int index, int value) => buttons.SetValue(Enum.ToObject(buttons.GetType().GetElementType()!, value), index);

    private void CheckScanTarget(bool right)
    {
        int width = Ultrawide.WidthFor(HelpOptions.Session(game).Settings.Resolution.Width, HelpOptions.Session(game).Settings.Resolution.Height);
        var original = (Matrix)camera.GetField("m_camera")!.GetValue(null)!;
        var expanded = Ultrawide.WidenCamera(original, width, GameAccess.Get<int>(level!, "Width") * 88);
        var targetPosition = Vector2.Transform(new Vector2(right ? width - 70 : 70, 300), Matrix.Invert(expanded));
        int x = (int)(targetPosition.X / 88), y = (int)(targetPosition.Y / 88);
        var tiles = GameAccess.Get<Array>(level!, "tiles");
        var tile = tiles.Cast<object>().First(tile => tile is not null && GameAccess.Get<short>(tile, "m_enigma") >= 0 && GameAccess.Get(tile, "m_sprite") is not null);
        var previous = tiles.GetValue(x, y);
        tiles.SetValue(tile, x, y);
        var cursor = GameAccess.Get(level!, "m_sprCursor")!;
        var targetScreen = Vector2.Transform(new Vector2(x * 88 + 44, y * 88 + 44), expanded);
        float cursorX = targetScreen.X - (width - 1280) / 2f;
        GameAccess.Set(cursor, "m_sprX", cursorX);
        GameAccess.Set(cursor, "m_sprY", targetScreen.Y);
        var buttons = PadButtons();
        var saved = (Array)buttons.Clone();
        Array.Clear(buttons);
        Button(buttons, 15, 2); // Hold scan while invoking the original hit tester.
        try
        {
            Call(level!, "ProcessScanMode");
            Require(ReferenceEquals(GameAccess.Get(level!, "m_tilSelected"), tile), "scan did not select the tile in the added side strip");
            Require(GameAccess.Get<float>(cursor, "m_sprX") == cursorX, "scan cursor offset was not restored");
            Require((Matrix)camera.GetField("m_camera")!.GetValue(null)! == original && Ultrawide.GetScanWidth() == 1280, "scan camera context leaked");
            // Exercise original cursor clamps across both new screen edges.
            foreach (int direction in new[] { 0, 1 })
            {
                Button(buttons, direction, 2);
                for (int step = 0; step < width / 7 + 30; step++) Call(level!, "ProcessScanMode");
                float actual = GameAccess.Get<float>(cursor, "m_sprX") + (width - 1280) / 2f;
                float origin = GameAccess.Get<float>(cursor, "m_sprOriginX");
                Require(Math.Abs(actual - (direction == 0 ? origin - 70 : width - origin + 70)) < .01f, "scan cursor cannot reach the full canvas edge");
                Button(buttons, direction, 0);
            }
            GameAccess.Set(cursor, "m_sprX", cursorX);
        }
        finally { Array.Copy(saved, buttons, saved.Length); tiles.SetValue(previous, x, y); }
    }

    private void CheckZoomMove(object map)
    {
        var grid = GameAccess.Get<Array>(GameAccess.Get(map, "m_map")!, "m_zmGrilleContent");
        var start = GameAccess.Get<Point>(map, "m_posCase");
        var offsets = new[] { new Point(-1, 0), new Point(1, 0), new Point(0, -1), new Point(0, 1) };
        int direction = Array.FindIndex(offsets, offset => start.X + offset.X >= 0 && start.X + offset.X < grid.GetLength(1) &&
            start.Y + offset.Y >= 0 && start.Y + offset.Y < grid.GetLength(0) && grid.GetValue(start.Y + offset.Y, start.X + offset.X)!.ToString() != "STOP");
        Require(direction >= 0, "zoom map has no traversable start neighbor");
        var buttons = PadButtons();
        var saved = (Array)buttons.Clone();
        Array.Clear(buttons);
        try
        {
            var state = map.GetType().GetField("m_state")!;
            state.SetValue(map, Enum.Parse(state.FieldType, "SELECTION"));
            Button(buttons, direction, 2);
            Call(map, "GAZM_Process");
            Button(buttons, direction, 0);
            var before = GameAccess.Get<Vector2>(map, "m_posCoord");
            Call(map, "GAZM_Process");
            Require(GameAccess.Get<Vector2>(map, "m_posCoord") != before, "original zoom-map movement did not advance");
        }
        finally { Array.Copy(saved, buttons, saved.Length); }
    }
}
