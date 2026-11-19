using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using SDL3;

// Preserve pixel edges through FNA's final backbuffer-to-window scaling too.
Environment.SetEnvironmentVariable("FNA3D_BACKBUFFER_SCALE_NEAREST",
    Environment.GetEnvironmentVariable("FNA3D_BACKBUFFER_SCALE_NEAREST") ?? "1");

if (args.Length == 0) args = ["--run"];
string? saveDirectory = null;
if (args.Length < 1 || args[0] is not ("--run" or "--check-load" or "--probe-content" or "--probe-audio" or "--probe-menu-audio" or "--probe-save" or "--probe-ultrawide" or "--probe-prompts" or "--probe-signs"))
{
    Console.Error.WriteLine("Usage: Port.Launcher --run [--save-dir <folder>] [--frames N] [--scripted-controller [--play-frames N]] [--scripted-save complete|continue] [--scripted-desktop] | --check-load | --probe-content | --probe-audio | --probe-menu-audio | --probe-save <read|write|reset> <isolated-save-directory> | --probe-ultrawide <capture-directory> | --probe-prompts <capture-directory> | --probe-signs <capture-directory> [text-filter]");
    return 2;
}
int? limit = null;
bool scripted = false;
int playFrames = 420;
bool customPlayFrames = false;
string? saveScript = null;
bool scriptedDesktop = false;
if (args[0] == "--probe-save" && (args.Length != 3 || args[1] is not ("read" or "write" or "reset")))
{
    Console.Error.WriteLine("--probe-save requires read, write, or reset and an isolated save directory.");
    return 2;
}
if (args[0] is "--probe-ultrawide" or "--probe-prompts" && args.Length != 2 ||
    args[0] == "--probe-signs" && args.Length is not (2 or 3))
{
    Console.Error.WriteLine($"{args[0]} requires a capture directory.");
    return 2;
}
for (int index = 1; args[0] is not ("--probe-save" or "--probe-ultrawide" or "--probe-prompts" or "--probe-signs") && index < args.Length; index++)
{
    if (args[0] == "--run" && args[index] == "--frames" && index + 1 < args.Length &&
        int.TryParse(args[++index], out var parsed) && parsed > 0 && limit is null)
        limit = parsed;
    else if (args[0] == "--run" && args[index] == "--save-dir" && index + 1 < args.Length && saveDirectory is null)
        saveDirectory = args[++index];
    else if (args[0] == "--run" && args[index] == "--scripted-controller" && !scripted)
        scripted = true;
    else if (args[0] == "--run" && args[index] == "--scripted-desktop" && !scriptedDesktop)
        scriptedDesktop = true;
    else if (args[0] == "--run" && args[index] == "--scripted-save" && saveScript is null &&
             index + 1 < args.Length && args[index + 1] is "complete" or "continue")
    {
        saveScript = args[++index];
        scripted = true;
    }
    else if (args[0] == "--run" && args[index] == "--play-frames" && index + 1 < args.Length &&
        int.TryParse(args[++index], out var parsedPlayFrames) && parsedPlayFrames >= 420 && !customPlayFrames)
    {
        playFrames = parsedPlayFrames;
        customPlayFrames = true;
    }
    else
    {
        Console.Error.WriteLine("Invalid launcher arguments.");
        return 2;
    }
}
if (customPlayFrames && !scripted)
{
    Console.Error.WriteLine("--play-frames requires --scripted-controller.");
    return 2;
}
if (scriptedDesktop && scripted)
{
    Console.Error.WriteLine("Use one scripted input driver per run.");
    return 2;
}

try
{
    var gameDirectory = AppContext.BaseDirectory;
    if (args[0] == "--run")
    {
        // Also honor setup's chosen save folder when the EXE is opened directly.
        var locations = Path.GetFullPath(Path.Combine(gameDirectory, "..", "..", "..", "locations.ini"));
        if (saveDirectory is null && File.Exists(locations))
            saveDirectory = File.ReadLines(locations).FirstOrDefault(line => line.StartsWith("Saves=", StringComparison.OrdinalIgnoreCase))?[6..];
        if (!string.IsNullOrWhiteSpace(saveDirectory))
            Environment.SetEnvironmentVariable("PIXEL_PORT_SAVE_DIR", Path.GetFullPath(saveDirectory));
#if !DEBUG
        if (args.Length == 1 || saveDirectory is not null)
        {
            var logs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pixel Recompilation", "Logs");
            Directory.CreateDirectory(logs);
            var log = TextWriter.Synchronized(new StreamWriter(Path.Combine(logs, "run.log"), append: false) { AutoFlush = true });
            Console.SetOut(log);
            Console.SetError(log);
        }
#endif
    }
    var assemblyPath = Path.Combine(gameDirectory, "Arkedo Series - 03 PIXEL!.exe");
    var manifestPath = Path.GetFullPath(Path.Combine(gameDirectory, "..", "..", "manifest.json"));
    using var manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
    var entry = manifest.RootElement.GetProperty("files").EnumerateArray()
        .Single(row => row.GetProperty("path").GetString() == "584E07D1/Arkedo Series - 03 PIXEL!.exe");
    var expected = entry.GetProperty("preparedSha256").GetString();
    var actual = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assemblyPath)));
    if (actual != expected) throw new InvalidDataException("Prepared executable differs from its manifest.");
    using (var pe = new PEReader(File.OpenRead(assemblyPath)))
        Console.WriteLine($"ASSEMBLY_PE machine={pe.PEHeaders.CoffHeader.Machine} corFlags={pe.PEHeaders.CorHeader?.Flags}");
    AssemblyLoadContext.Default.Resolving += (_, name) =>
    {
        var candidate = Path.Combine(gameDirectory, name.Name + ".dll");
        return File.Exists(candidate) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate) : null;
    };
    var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
    var gameType = assembly.GetType("Scale.GameScale", throwOnError: true)!;
    if (!typeof(Game).IsAssignableFrom(gameType)) throw new InvalidDataException("Prepared GameScale is not an FNA Game.");
    var types = assembly.GetTypes();
    Console.WriteLine($"LAUNCH_LOAD_OK types={types.Length} fna={typeof(Game).Assembly.GetName().Version}");
    if (args[0] == "--probe-save")
    {
        SaveCoverage.Run(assembly, args[1], args[2]);
        return 0;
    }
    if (args[0] is "--probe-content" or "--probe-prompts")
    {
        using var coverage = new ContentCoverageGame(Path.Combine(gameDirectory, "Content"), args[0] == "--probe-prompts" ? args[1] : null);
        coverage.Run();
        return 0;
    }
    if (args[0] == "--probe-audio")
    {
        AudioCoverage.Run(Path.Combine(gameDirectory, "Content", "Sounds"));
        return 0;
    }
    if (args[0] == "--probe-menu-audio")
    {
        AudioCoverage.MenuOnly(Path.Combine(gameDirectory, "Content", "Sounds"));
        return 0;
    }
    if (args[0] == "--check-load")
    {
        int jitted = 0, failed = 0;
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var type in types)
            foreach (var method in type.GetMethods(flags).Cast<MethodBase>().Concat(type.GetConstructors(flags)))
            {
                if (method.IsAbstract || method.ContainsGenericParameters || method.GetMethodBody() is null) continue;
                try
                {
                    RuntimeHelpers.PrepareMethod(method.MethodHandle);
                    jitted++;
                }
                catch (Exception error)
                {
                    failed++;
                    Console.Error.WriteLine($"JIT_FAIL {type.FullName}.{method.Name}: {error.GetType().Name}: {error.Message}");
                }
            }
        Console.WriteLine($"CHECK_LOAD_RESULT jitted={jitted} failed={failed}");
        return failed == 0 ? 0 : 1;
    }
    using var game = (Game)Activator.CreateInstance(gameType)!;
    if (args[0] == "--probe-signs")
    {
        Environment.SetEnvironmentVariable("PIXEL_PORT_SAVE_DIR", Path.Combine(Path.GetFullPath(args[1]), "saves"));
        var probe = new SignProbe(game, args[1], args.Length == 3 ? args[2] : null);
        game.Components.Add(probe);
        game.Run();
        probe.Verify();
        return 0;
    }
    if (args[0] == "--probe-ultrawide")
    {
        Environment.SetEnvironmentVariable("PIXEL_PORT_SAVE_DIR", Path.Combine(Path.GetFullPath(args[1]), "saves"));
        var probe = new UltrawideProbe(game, args[1]);
        game.Components.Add(probe);
        game.Run();
        probe.Verify();
        return 0;
    }
    if ((scripted || scriptedDesktop) && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PIXEL_PORT_SAVE_DIR")))
        Environment.SetEnvironmentVariable("PIXEL_PORT_SAVE_DIR", Path.GetFullPath(Path.Combine(gameDirectory, "..", "..", "probe-saves")));
    using var virtualPad = scripted ? new VirtualGamepad() : null;
    var session = new SessionProbe(game, gameType, limit, virtualPad, playFrames, saveScript, scriptedDesktop);
    game.Components.Add(session);
    Console.WriteLine($"CONTROLLER connected={GamePad.GetCapabilities(PlayerIndex.One).IsConnected}");
    Console.WriteLine("GAME_RUN_START");
    game.Run();
    if (scripted || scriptedDesktop) session.VerifyScript();
    Console.WriteLine("GAME_RUN_EXIT clean");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine($"LAUNCH_FAIL {error}");
    return 1;
}

internal sealed class SessionProbe : GameComponent
{
    private readonly Game game;
    private readonly FieldInfo stateField;
    private readonly int? limit;
    private readonly VirtualGamepad? virtualPad;
    private readonly int playFrames;
    private string? lastState;
    private string? lastSubstate;
    private bool? lastPause;
    private int frames;
    private int gameFrames;
    private int titleFrames;
    private int pressCountdown;
    private int lastNewGameFrame;
    private SDL.SDL_GamepadButton? pressed;
    private bool startSent, introSent, pauseSent, resumeSent, secondPauseSent, quitSent, titleExitSent;
    private bool sawGame, sawPause, sawResume, sawReturn;
    private readonly SaveProgressionProbe? saveProgression;
    private bool progressionReady;
    private readonly DesktopInputScript? desktopInput;
    private int optionsControllerStep, optionsControllerAge;
    private bool optionsControllerDone;

    public SessionProbe(Game game, Type gameType, int? limit, VirtualGamepad? virtualPad, int playFrames, string? saveScript, bool scriptedDesktop) : base(game)
    {
        this.game = game;
        this.limit = limit;
        this.virtualPad = virtualPad;
        this.playFrames = playFrames;
        if (saveScript is not null) saveProgression = new SaveProgressionProbe(game, saveScript, Press);
        if (scriptedDesktop) desktopInput = new DesktopInputScript(game);
        stateField = gameType.GetField("m_state", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException("GameScale.m_state");
        UpdateOrder = int.MaxValue;
    }

    public override void Update(GameTime gameTime)
    {
        frames++;
        var state = stateField.GetValue(game)?.ToString();
        if (state != lastState)
        {
            Console.WriteLine($"GAME_STATE frame={frames} state={state}");
            lastState = state;
            lastSubstate = null;
            if (state == "GAME") gameFrames = 0;
            if (state == "TITLE") titleFrames = 0;
            if (state == "GAME") sawGame = true;
            if (state == "TITLE" && sawGame) sawReturn = true;
        }
        var component = game.GetType().GetField("m_stateComponent", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(game);
        if (frames % 120 == 0 && component?.GetType().GetField("m_sndMusique", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(component)
            is Microsoft.Xna.Framework.Audio.Cue music)
            Console.WriteLine($"GAME_MUSIC frame={frames} state={state} name={music.Name} playing={music.IsPlaying} paused={music.IsPaused} stopped={music.IsStopped} disposed={music.IsDisposed}");
        var substate = component?.GetType().GetField("m_menuState", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(component)?.ToString()
            ?? component?.GetType().GetField("m_state", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(component)?.ToString();
        if (substate is not null && substate != lastSubstate)
        {
            Console.WriteLine($"GAME_SUBSTATE frame={frames} state={state} substate={substate}");
            lastSubstate = substate;
        }
        if (state == "GAME")
        {
            gameFrames++;
            var level = component?.GetType().GetField("m_level", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(component);
            var pause = level?.GetType().GetField("m_pause", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(level) as bool?;
            if (pause is not null && pause != lastPause)
            {
                Console.WriteLine($"GAME_PAUSE frame={frames} paused={pause.Value}");
                lastPause = pause;
                if (pause.Value) sawPause = true;
                if (!pause.Value && sawPause) sawResume = true;
            }
        }
        if (state == "TITLE") titleFrames++;
        if (virtualPad is not null) DriveVirtualPad(state, substate, component);
        desktopInput?.Update(frames, state, substate, component);
        if (limit is int max && frames >= max) game.Exit();
        base.Update(gameTime);
    }

    public void VerifyScript()
    {
        if (desktopInput is not null) { desktopInput.Verify(); return; }
        saveProgression?.Verify();
        if (!sawGame || !sawPause || !sawResume || !sawReturn || !titleExitSent)
            throw new InvalidOperationException($"Script incomplete: game={sawGame}, pause={sawPause}, resume={sawResume}, title={sawReturn}, exit={titleExitSent}.");
        Console.WriteLine("SCRIPT_RESULT game=ok pause=ok resume=ok title=ok exit=ok");
    }

    private void DriveVirtualPad(string? state, string? substate, object? component)
    {
        if (pressCountdown > 0)
        {
            if (--pressCountdown == 0 && pressed is SDL.SDL_GamepadButton button)
            {
                virtualPad!.SetButton(button, false);
                pressed = null;
            }
            return;
        }
        if (pressed is not null) return;
        if (saveProgression is null && !optionsControllerDone && DriveHelpController(state, substate, component)) return;
        if (saveProgression is not null && !progressionReady && saveProgression.Handle(state, substate, component))
        {
            if (saveProgression.Ready)
            {
                progressionReady = true;
                gameFrames = 0;
            }
            return;
        }
        if (state == "START" && substate == "START" && !startSent && frames > 500)
        {
            startSent = true;
            Press(SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_START, "press start");
        }
        else if (state == "START" && substate == "SELECTION" && frames > 550 &&
                 (lastNewGameFrame == 0 || frames - lastNewGameFrame > 90))
        {
            lastNewGameFrame = frames;
            Press(SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH, "new game");
        }
        else if (state == "INTRO" && substate == "PAGE2" && !introSent &&
                 component?.GetType().GetField("m_timer", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(component) is int timer && timer == 0)
        {
            introSent = true;
            Press(SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH, "start level");
        }
        else if (state == "GAME" && gameFrames >= 300 && gameFrames < playFrames - 60 && gameFrames % 180 == 20)
            Press(SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_RIGHT, "move right");
        else if (state == "GAME" && gameFrames >= 300 && gameFrames < playFrames - 60 && gameFrames % 180 == 80)
            Press(SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH, "jump/action");
        else if (state == "GAME" && gameFrames >= 300 && gameFrames < playFrames - 60 && gameFrames % 180 == 140)
            Press(SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_LEFT, "move left");
        else if (state == "GAME" && gameFrames > playFrames && !pauseSent)
        {
            pauseSent = true;
            Press(SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_START, "pause");
        }
        else if (state == "GAME" && gameFrames > playFrames + 80 && !resumeSent)
        {
            resumeSent = true;
            Press(SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_START, "resume");
        }
        else if (state == "GAME" && gameFrames > playFrames + 180 && !secondPauseSent)
        {
            secondPauseSent = true;
            Press(SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_START, "pause before title");
        }
        else if (state == "GAME" && gameFrames > playFrames + 230 && !quitSent)
        {
            quitSent = true;
            Press(SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_BACK, "return to title");
        }
        else if (state == "TITLE" && titleFrames > 100 && !titleExitSent)
        {
            titleExitSent = true;
            Press(SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_BACK, "exit");
        }
    }

    private void Press(SDL.SDL_GamepadButton button, string action)
    {
        virtualPad!.SetButton(button, true);
        pressed = button;
        pressCountdown = 5;
        Console.WriteLine($"SCRIPT_CONTROLLER frame={frames} action={action}");
        var padType = game.GetType().Assembly.GetType("LowLevel.CLASS_GAMEPAD")!;
        var pad = padType.GetField("m_gpad")!.GetValue(null);
        Console.WriteLine($"SCRIPT_PLAYER selected={padType.GetField("m_gpadPlayerIndex")!.GetValue(pad)} active={game.IsActive}");
    }

    private bool DriveHelpController(string? state, string? substate, object? component)
    {
        if (optionsControllerStep == 0 && (state != "START" || substate != "SELECTION" || frames <= 550)) return false;
        if (++optionsControllerAge < 10) return true;
        var options = Port.Hooks.HelpOptions.Session(game);
        void Push(SDL.SDL_GamepadButton button, string action)
        {
            Press(button, action);
            optionsControllerStep++;
            optionsControllerAge = 0;
        }
        var a = SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH;
        var b = SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_EAST;
        var down = SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_DOWN;
        switch (optionsControllerStep)
        {
            case 0:
                if (Port.Hooks.GameAccess.Get<int>(component!, "m_menuChoix") != 2)
                { Press(down, "select help"); optionsControllerAge = 0; }
                else Push(a, "open help");
                break;
            case 1:
                Require(options.Page == Port.Hooks.HelpPage.Home, "open help"); Push(a, "cycle display"); break;
            case 2:
                Require(options.Settings.Mode == Port.Hooks.DisplayMode.Fullscreen, "apply display immediately");
                Push(SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_LEFT, "restore borderless"); break;
            case 3: Push(down, "select resolution"); break;
            case 4: Push(SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_LEFT, "cycle resolution"); break;
            case 5: Push(SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_RIGHT, "restore resolution"); break;
            case 6: Push(down, "select credits"); break;
            case 7: Push(a, "open credits"); break;
            case 8 when state == "CREDITS" && substate == "CREDITS": Push(b, "back from credits"); break;
            case 9 when state == "TITLE" && options.Page == Port.Hooks.HelpPage.Home:
                Push(b, "back from help"); break;
            case 10 when !options.IsOpen:
                if (Port.Hooks.GameAccess.Get<int>(component!, "m_menuChoix") != 0)
                { Press(SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_UP, "select new game"); optionsControllerAge = 0; }
                else
                {
                    optionsControllerDone = true;
                    // The existing driver accepts New Game on START; preserve its normal transition path.
                    Press(a, "new game after help");
                    Console.WriteLine("OPTIONS_CONTROLLER_RESULT display=ok resolution=ok credits=ok back=ok");
                }
                break;
        }
        return true;
        static void Require(bool condition, string action)
        {
            if (!condition) throw new InvalidOperationException("Controller Help/Options fixture failed: " + action);
        }
    }
}

internal sealed unsafe class VirtualGamepad : IDisposable
{
    private readonly uint id;
    private readonly IntPtr joystick;
    public VirtualGamepad()
    {
        var desc = new SDL.SDL_VirtualJoystickDesc
        {
            version = (uint)sizeof(SDL.SDL_VirtualJoystickDesc),
            type = (ushort)SDL.SDL_JoystickType.SDL_JOYSTICK_TYPE_GAMEPAD,
            naxes = 6,
            nbuttons = (ushort)SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_COUNT
        };
        id = SDL.SDL_AttachVirtualJoystick(ref desc);
        if (id == 0) throw new InvalidOperationException($"SDL virtual gamepad attach failed: {SDL.SDL_GetError()}");
        joystick = SDL.SDL_OpenJoystick(id);
        if (joystick == IntPtr.Zero) throw new InvalidOperationException($"SDL virtual gamepad open failed: {SDL.SDL_GetError()}");
        Console.WriteLine($"VIRTUAL_CONTROLLER attached={id}");
    }
    public void SetButton(SDL.SDL_GamepadButton button, bool down)
    {
        if (!SDL.SDL_SetJoystickVirtualButton(joystick, (int)button, down))
            throw new InvalidOperationException($"SDL virtual button failed: {SDL.SDL_GetError()}");
    }
    public void Dispose()
    {
        SDL.SDL_CloseJoystick(joystick);
        SDL.SDL_DetachVirtualJoystick(id);
    }
}
