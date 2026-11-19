using System.Reflection;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Port.Hooks;

// Internal capture fixture. Teleports to every sign (including concealed tiles)
// and invokes the original interaction; never traverses levels or writes player saves.
internal sealed class SignProbe : GameComponent
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly Game game;
    private readonly string directory;
    private readonly string? textFilter;
    private readonly Type camera;
    private readonly List<object> manifest = [];
    private readonly List<Sign> signs = [];
    private readonly int[] counts = new int[7];
    private KeyboardState keyboard;
    private object? component, level;
    private int frames, levelIndex, signIndex, variant, draws, captures;
    private bool posed, done;
    private Matrix expectedCamera;
    private sealed record Sign(object Tile, int X, int Y, bool Hidden);

    internal SignProbe(Game game, string directory, string? textFilter = null) : base(game)
    {
        this.game = game;
        this.directory = Path.GetFullPath(directory);
        this.textFilter = textFilter;
        Directory.CreateDirectory(this.directory);
        camera = game.GetType().Assembly.GetType("Scale.GAME.GAME_Camera")!;
        DesktopInput.ReadKeyboard = () => keyboard;
        DesktopInput.ReadMouse = () => default;
        DesktopInput.IsActive = _ => true;
        HelpOptions.CaptureFrame = Capture;
        UpdateOrder = int.MaxValue;
    }

    public override void Update(GameTime gameTime)
    {
        if (++frames > 30000) throw new InvalidOperationException("Sign capture timed out.");
        string state = GameAccess.Get(game, "m_state")!.ToString()!;
        component = GameAccess.Get(game, "m_stateComponent");
        if (state == "START" && component is not null)
        {
            string menu = GameAccess.Get(component, "m_menuState")!.ToString()!;
            if (menu == "START" && frames > 500) keyboard = new(Keys.Enter);
            if (menu == "SELECTION")
            {
                keyboard = default;
                GameAccess.Set(game, "m_levelIndexContinue", -1);
                var init = game.GetType().GetMethod("InitState", Flags)!;
                init.Invoke(game, [Enum.Parse(init.GetParameters()[0].ParameterType, "GAME")]);
            }
            return;
        }
        if (state != "GAME" || component is null || GameAccess.Get(component, "m_level") is not object loaded) return;
        level = loaded;
        ((GameComponent)component).Enabled = false;
        GameAccess.Set(component, "m_timerIntro", 0);
        if (done) { game.Exit(); return; }
        if (posed) return;
        if (GameAccess.Get<int>(component, "m_levelIndex") != levelIndex)
        {
            Call(level, "Dispose");
            var particles = game.GetType().Assembly.GetType("LowLevel.CLASS_PARTICULE")!;
            particles.GetMethod("Ark_PartKillAllGenerator")!.Invoke(null, null);
            particles.GetMethod("Ark_PartKillAllBoulette")!.Invoke(null, null);
            GameAccess.Set(component, "m_levelIndex", levelIndex);
            Call(component, "InitLevel");
            level = GameAccess.Get(component, "m_level")!;
        }
        if (signs.Count == 0) Discover();
        if (signs.Count == 0) { NextLevel(); return; }
        Pose();
        draws = 0;
        posed = true;
    }

    private void Discover()
    {
        var tiles = GameAccess.Get<Array>(level!, "tiles");
        for (int y = 0; y < tiles.GetLength(1); y++)
        for (int x = 0; x < tiles.GetLength(0); x++)
        {
            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            object? tile = tiles.GetValue(x, y);
            bool hidden = false;
            while (tile is not null && seen.Add(tile))
            {
                if (tile.GetType().Name == "GAME_Tile_Sign") signs.Add(new(tile, x, y, hidden));
                tile = GameAccess.Get(tile, "m_otherTile");
                hidden = true;
            }
        }
        if (textFilter is not null)
            signs.RemoveAll(sign => !GameAccess.Get<string>(sign.Tile, "m_text").Contains(textFilter, StringComparison.OrdinalIgnoreCase));
        counts[levelIndex] = signs.Count;
        Console.WriteLine($"SIGN_INVENTORY level={levelIndex} plaques={signs.Count} hidden={signs.Count(sign => sign.Hidden)}");
    }

    private void Pose()
    {
        bool wide = variant >= 4, controller = variant % 4 >= 2, open = variant % 2 == 1;
        Environment.SetEnvironmentVariable("PIXEL_PORT_DISABLE_DESKTOP_INPUT", controller ? "1" : null);
        keyboard = controller ? default : new(Keys.Z);
        var resolution = wide ? new Resolution(1680, 720) : new(1280, 720);
        OptionsSession.OutputSize = _ => new(resolution.Width, resolution.Height);
        var options = HelpOptions.Session(game);
        GameAccess.Set(options, "<Settings>k__BackingField", options.Settings with { Resolution = resolution });
        SetState("PLATFORM");
        GameAccess.Set(level!, "m_pause", false);
        Tint(GameAccess.Get(component!, "m_sprPause")!, Color.Transparent);
        var fade = game.GetType().Assembly.GetType("LowLevel.CLASS_MOD_FADE")!.GetField("m_fadeCurrent")!.GetValue(null)!;
        GameAccess.Set(fade, "m_fadeInc", 0f);
        GameAccess.Set(fade, "m_fadeAlpha", 0f);
        Tint(GameAccess.Get(fade, "m_sprFade")!, Color.Transparent);
        Tint(GameAccess.Get(level!, "m_sprQuadNoir")!, Color.Transparent);

        var sign = signs[signIndex];
        GameAccess.Get<Array>(level!, "tiles").SetValue(sign.Tile, sign.X, sign.Y);
        var player = GameAccess.Get(level!, "Player")!;
        var position = new Vector2(sign.X * 88 + 44, sign.Y * 88 + 87);
        GameAccess.Set(player, "Position", position);
        GameAccess.Set(player, "onSign", new Point(sign.X, sign.Y));
        Call(sign.Tile, "GATI_Activate");
        // Settle the original interaction badge's bounce at its final scale.
        var badge = GameAccess.Get(GameAccess.Get(sign.Tile, "m_decoTile")!, "m_decoTile")!;
        var sprite = GameAccess.Get(badge, "m_sprite")!;
        GameAccess.Set(sprite, "m_sprEftBounceOn", false);
        GameAccess.Set(sprite, "m_sprScaleX", 1f);
        GameAccess.Set(sprite, "m_sprScaleY", 1f);
        GameAccess.Set(sprite, "m_sprEftBounceDecScale", 0f);
        camera.GetField("m_scale")!.SetValue(null, 1f);
        camera.GetMethods().Single(method => method.Name == "GACM_UpdateCamera" && method.GetParameters().Length == 1).Invoke(null, [position]);
        if (open)
        {
            Call(level!, "StartSignMode");
            Tint(GameAccess.Get(level!, "m_sprQuadNoir")!, new Color(255, 255, 255, 170));
        }
        expectedCamera = (Matrix)camera.GetField("m_camera")!.GetValue(null)!;
    }

    private void Capture(Game _, Texture2D frame)
    {
        if (!posed || ++draws < 18) return;
        bool wide = variant >= 4, controller = variant % 4 >= 2, open = variant % 2 == 1;
        Require(frame.Width == (wide ? 1680 : 1280) && frame.Height == 720, "incorrect capture aspect ratio");
        Require((Matrix)camera.GetField("m_camera")!.GetValue(null)! == expectedCamera, "simulation camera changed");
        Require(Ultrawide.GetWidth() == 1280, "drawing context leaked");
        Require(DesktopInput.KeyboardHints(game) == !controller, "incorrect input hints");
        var sign = signs[signIndex];
        string original = GameAccess.Get<string>(sign.Tile, "m_text");
        string rendered = GameAccess.Get<string>(GameAccess.Get(level!, "m_fntTextBox")!, "m_fntText");
        if (open) Require(rendered == TutorialHints.Translate(original, !controller), "incorrect sign text");
        string aspect = wide ? "21x9" : "16x9", input = controller ? "controller" : "keyboard", scene = open ? "open" : "prompt";
        string name = $"level-{levelIndex}-plaque-{signIndex + 1:D2}-{input}-{scene}-{aspect}.png";
        using (var output = File.Create(Path.Combine(directory, name))) frame.SaveAsPng(output, frame.Width, frame.Height);
        manifest.Add(new { level = levelIndex, plaque = signIndex + 1, sign.X, sign.Y, sign.Hidden, aspect, input, scene, file = name, original, rendered = open ? rendered : null });
        Console.WriteLine($"SIGN_CAPTURE {name}");
        captures++;
        posed = false;
        if (++variant == 8)
        {
            variant = 0;
            Call(sign.Tile, "GATI_Deactivate");
            if (++signIndex == signs.Count) NextLevel();
        }
    }

    private void NextLevel()
    {
        signs.Clear();
        signIndex = 0;
        if (++levelIndex == 7) done = true;
    }

    private void SetState(string state) => GameAccess.Set(level!, "m_state", Enum.Parse(level!.GetType().GetField("m_state")!.FieldType, state));
    private static void Tint(object sprite, Color color)
    {
        GameAccess.Set(sprite, "m_sprRGBA", color);
        GameAccess.Set(sprite, "m_sprEftFadeOn", false);
    }
    private static void Call(object instance, string method) => instance.GetType().GetMethod(method, Flags)!.Invoke(instance, null);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Sign capture: " + message);
    }
    internal void Verify()
    {
        Require(done && captures == counts.Sum() * 8 && captures > 0, "incomplete sign captures");
        File.WriteAllText(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(new { counts, captures, images = manifest }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"SIGN_PROBE_RESULT levels=7 plaques={counts.Sum()} captures={captures} aspects=16:9,21:9 input=keyboard,controller scenes=prompt,open");
    }
}
