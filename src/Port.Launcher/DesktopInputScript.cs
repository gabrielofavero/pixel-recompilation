using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Port.Hooks;
using Texture2D = Microsoft.Xna.Framework.Graphics.Texture2D;
using ContentManager = Microsoft.Xna.Framework.Content.ContentManager;

// Raw device-state fixture. Everything after sampling uses the shipped hook and original game reader.
internal sealed class DesktopInputScript
{
    private readonly Game game;
    private KeyboardState keyboard;
    private MouseState mouse = MouseAt(0, 0);
    private bool focused = true;
    private int step, age;
    private float startX, jumpY;
    private int continuePlayFrames;
    private int continueBackPhase;
    private int escapePhase;
    private int confirmationBackPhase;
    private bool captureMenus, capturedHelpSelection;
    private int optionsStep, optionsAge, optionsBackAttempt;
    private bool optionsEscapeChecked, optionsEscapeReopened;
    private readonly bool ultrawide = Environment.GetEnvironmentVariable("PIXEL_PORT_SCRIPTED_ULTRAWIDE") == "1";

    internal DesktopInputScript(Game game)
    {
        this.game = game;
        if (ultrawide) OptionsSession.OutputSize = _ => new(3440, 1440);
        DesktopInput.ReadKeyboard = () => keyboard;
        DesktopInput.ReadMouse = () =>
        {
            var options = HelpOptions.Session(game);
            var bounds = options.Destination(options.CanvasWidth);
            return new MouseState(bounds.X + (int)Math.Ceiling((mouse.X + (options.CanvasWidth - 1280) / 2d) * bounds.Width / options.CanvasWidth),
                bounds.Y + (int)Math.Ceiling(mouse.Y * bounds.Height / 720d), mouse.ScrollWheelValue,
                mouse.LeftButton, mouse.MiddleButton, mouse.RightButton, mouse.XButton1, mouse.XButton2);
        };
        DesktopInput.IsActive = _ => focused;
        if (Environment.GetEnvironmentVariable("PIXEL_PORT_CAPTURE_OPTIONS") is string captureDirectory)
        {
            captureMenus = true;
            Directory.CreateDirectory(captureDirectory);
            HashSet<string> captured = [];
            HelpOptions.CaptureFrame = (_, frame) =>
            {
                var page = HelpOptions.Session(game).Page;
                bool main = page == HelpPage.Closed && step is 2 or 3;
                string name = main ? step == 3 ? "Help-selected" : "Main" : page == HelpPage.Home
                    ? $"Home-{HelpOptions.Session(game).Selected}" : page.ToString();
                if ((main ? age : optionsAge) < 1 || !captured.Add(name)) return;
                using var output = File.Create(Path.Combine(captureDirectory, name + ".png"));
                frame.SaveAsPng(output, frame.Width, frame.Height);
                if (name == "Help-selected") capturedHelpSelection = true;
            };
        }
    }

    private static MouseState MouseAt(int x, int y, bool down = false) => new(x, y, 0,
        down ? ButtonState.Pressed : ButtonState.Released, ButtonState.Released, ButtonState.Released,
        ButtonState.Released, ButtonState.Released);
    private static T Field<T>(object instance, string name) => GameAccess.Get<T>(instance, name);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Desktop input fixture: " + message);
    }
    private void Next(string result)
    {
        Console.WriteLine($"DESKTOP_SCRIPT step={step} result={result}");
        step++;
        age = 0;
        keyboard = default;
        mouse = MouseAt(mouse.X, mouse.Y);
    }
    private void Point(string id, string state, string substate, object? component, bool paused)
    {
        var target = MenuTargets.Build(state, substate, component, paused).Single(target => target.Id == id);
        mouse = MouseAt(target.Bounds.Center.X, target.Bounds.Center.Y);
    }
    private void Click() => mouse = MouseAt(mouse.X, mouse.Y, true);
    private bool Down(int index) => (Convert.ToInt32(((Array)GameAccess.Get(
        game.GetType().Assembly.GetType("LowLevel.CLASS_GAMEPAD")!.GetField("m_gpad")!.GetValue(null)!, "m_gpadButtons")!).GetValue(index)) & 2) != 0;

    internal void Update(int frame, string? state, string? substate, object? component)
    {
        // The original gameplay loop also reads FNA focus directly. Keep it in
        // sync with the simulated device focus so background probes cannot stall
        // the level intro while the desktop hook still accepts fixture input.
        GameAccess.Set(game, "IsActive", focused);
        age++;
        var level = state == "GAME" && component is not null ? GameAccess.Get(component, "m_level") : null;
        bool paused = level is not null && Field<bool>(level, "m_pause");
        object? player = level is null ? null : GameAccess.Get(level, "Player");
        switch (step)
        {
            case 0 when state == "START" && substate == "START" && frame > 500:
                Next("press-and-hold-enter"); keyboard = new(Keys.Enter); break;
            case 1 when age >= 24:
                Require(state == "START" && substate == "SELECTION", "held Enter clicked through the start prompt");
                Require(Field<bool>(component!, "m_continue"), "seed this isolated run with --probe-save write first");
                Next("start-hold-does-not-click-through"); break;
            case 2 when age > 3:
                Require(state == "START" && substate == "SELECTION", "start prompt left the title menu");
                Point("title-2", state!, substate!, component, false); Next("hover-help-options"); break;
            case 3 when age > 8:
                if (captureMenus && !capturedHelpSelection) break;
                Require(Field<int>(component!, "m_menuChoix") == 2, "mouse hover did not select Help and Options");
                var titleContent = Field<ContentManager>(component!, "m_content");
                var titleSprites = Field<object[]>(component!, "m_sprMenuChoix");
                foreach (var (index, asset) in new[] { (0, "TITLE/TITLE_Menu_new_game"), (1, "TITLE/TITLE_Menu_continue") })
                    Require(ReferenceEquals(Field<Texture2D>(titleSprites[index], "m_texture"), titleContent.Load<Texture2D>(asset)),
                        "main menu replaced original label artwork: " + asset);
                Next("click-help-options"); Click(); break;
            case 4:
                DriveOptions(state); break;
            case 5:
                if (age <= 8) keyboard = default;
                else { Next("escape-back-from-credits"); keyboard = new(Keys.Escape); }
                break;
            case 6 when state == "TITLE" && substate == "SELECTION" && age > 12:
                if (HelpOptions.Session(game).IsOpen)
                {
                    keyboard = new(Keys.Back);
                    break;
                }
                Next("keyboard-navigation-under-stationary-mouse"); keyboard = new(Keys.W); break;
            case 7 when age > 4:
                Require(Field<int>(component!, "m_menuChoix") == 1,
                    $"stationary mouse stole keyboard navigation: selected={Field<int>(component!, "m_menuChoix")} page={HelpOptions.Session(game).Page} up={Down(2)}");
                Next("keyboard-retains-selection"); break;
            case 8 when age > 3:
                Point("title-0", state!, substate!, component, false); Next("hover-new-game"); break;
            case 9 when age > 3:
                Require(Field<int>(component!, "m_menuChoix") == 0, $"New Game hover failed: selected={Field<int>(component!, "m_menuChoix")} logical={HelpOptions.Session(game).LogicalMouse(DesktopInput.ReadMouse())} page={HelpOptions.Session(game).Page}");
                Next("click-new-game"); Click(); break;
            case 10 when substate == "CONFIRM" && age > 8:
                Next("confirmation-open"); keyboard = new(Keys.Escape); break;
            case 11 when age > 8:
                if (confirmationBackPhase == 0)
                {
                    Require(substate == "SELECTION", "Escape did not cancel the confirmation dialog");
                    Require(Field<bool>(component!, "m_continue"), "Escape cancellation reset the save");
                    keyboard = new(Keys.Enter); confirmationBackPhase++; age = 0; break;
                }
                keyboard = default;
                Require(substate == "CONFIRM", "confirmation did not reopen after Escape cancellation");
                Point("m_sprBoutonB", state!, substate!, component, false); Next("hover-cancel"); break;
            case 12 when age > 3:
                Next("click-cancel"); Click(); break;
            case 13 when substate == "SELECTION" && age > 8:
                Require(Field<bool>(component!, "m_continue"), "cancel reset the save");
                Next("cancel-preserves-progress"); break;
            case 14 when age > 3:
                Point("title-1", state!, substate!, component, false); Next("hover-continue"); break;
            case 15 when age > 3:
                Next("click-continue"); Click(); break;
            case 16 when state == "CONTINUE" && substate == "SELECTION" && age > 8:
                Next("continue-menu-open"); break;
            case 17 when age > 3:
                // The seeded fixture unlocks three entries; index 5 remains locked.
                var sprites = Field<object[]>(component!, "m_sprMenuChoix");
                var bounds = GameAccess.SpriteBounds(sprites[5], 42);
                mouse = MouseAt(bounds.Center.X, bounds.Center.Y);
                Next("click-locked-level"); Click(); break;
            case 18 when age > 8:
                Require(state == "CONTINUE" && substate == "SELECTION", "locked level was clickable");
                Next("locked-level-ignored"); break;
            case 19 when age > 3:
                if (continueBackPhase == 0)
                {
                    Point("levels-back", state!, substate!, component, false);
                    Click(); continueBackPhase++; age = 0;
                }
                else
                {
                    mouse = MouseAt(mouse.X, mouse.Y);
                    if (continueBackPhase == 1 && state == "TITLE" && substate == "SELECTION" && age > 8)
                    {
                        Point("title-1", state, substate, component, false);
                        Click(); continueBackPhase++; age = 0;
                    }
                    else if (continueBackPhase == 2 && state == "CONTINUE" && substate == "SELECTION" && age > 8)
                    {
                        Console.WriteLine("CONTINUE_MOUSE_BACK_RESULT back=ok reopen=ok");
                        Point("level-0", state, substate, component, false); Next("hover-unlocked-level");
                    }
                }
                break;
            case 20 when age > 3:
                Require(Field<int>(component!, "m_menuChoix") == 0, "level hover did not update selection");
                Next("click-unlocked-level"); Click(); break;
            case 21 when state == "GAME" && level is not null && Field<int>(component!, "m_timerIntro") == 0:
                mouse = MouseAt(mouse.X, mouse.Y);
                if (++continuePlayFrames < 60) break;
                if (!paused) keyboard = new(Keys.Escape);
                else
                {
                    keyboard = default;
                    if (!Field<bool>(GameAccess.Get(component!, "m_sprPause")!, "m_sprEftFadeOn"))
                    {
                        keyboard = new(Keys.Back);
                    }
                }
                break;
            case 21 when state == "TITLE" && substate == "SELECTION" && age > 8:
                Next("unlocked-level-click-and-backspace-to-title-ok"); break;
            case 22 when age > 3:
                Point("title-0", state!, substate!, component, false); Next("new-game-again"); break;
            case 23 when age > 3:
                Next("click-new-game-again"); Click(); break;
            case 24 when substate == "CONFIRM" && age > 8:
                Next("confirmation-again"); break;
            case 25 when age > 3:
                Point("m_sprBoutonA", state!, substate!, component, false); Next("hover-confirm"); break;
            case 26 when age > 3:
                Next("confirm-new-game"); Click(); break;
            case 27:
                mouse = MouseAt(mouse.X, mouse.Y);
                if (state == "INTRO" && substate == "PAGE2" && Field<int>(component!, "m_timer") == 0)
                {
                    Next("mouse-start-level"); Point("lets-go", state, substate, component, false); Click();
                }
                break;
            case 28 when state == "GAME" && level is not null && Field<int>(component!, "m_timerIntro") == 0 && age > 250:
                startX = Field<Vector2>(player!, "Position").X;
                Next("run-right"); keyboard = new(Keys.D, Keys.LeftShift); break;
            case 29 when age > 28:
                if (ultrawide) Require(HelpOptions.Session(game).CanvasWidth == 1720, "platforming did not render at 21:9");
                Require(Down(1) && Down(10), "move/run did not reach original input slots");
                Require(Field<Vector2>(player!, "Position").X > startX + 10, "keyboard did not move the player");
                jumpY = Field<Vector2>(player!, "Position").Y;
                Next("space-jump"); keyboard = new(Keys.Space); break;
            case 30 when age > 8:
                Require(Down(8) && !Down(17), "Space jumped and paused together");
                Require(Field<Vector2>(player!, "Position").Y < jumpY, "Space did not lift the player");
                Next("jump-ok"); break;
            case 31 when age > 12:
                Next("scan-mouse-interact-special"); keyboard = new(Keys.Tab, Keys.Q); Click(); break;
            case 32 when age > 4:
                Require(Down(15) && Down(9) && Down(11), "scan/mouse B/special mappings failed");
                Next("bindings-ok"); break;
            case 33 when age > 8:
                Require(!Down(9), "mouse release did not release B");
                Next("focus-lost"); focused = false; keyboard = new(Keys.D); break;
            case 34 when age > 8:
                if (ultrawide) Require(HelpOptions.Session(game).CanvasWidth == 1720, "pause changed the gameplay canvas width");
                Require(paused && !Down(1), "focus loss did not suppress input and pause");
                Next("focus-restored-with-held-input"); focused = true; keyboard = new(Keys.Enter); Click(); break;
            case 35 when age > 12:
                Require(paused && !Down(8) && !Down(9), "held input activated after focus returned");
                Next("focus-requires-release"); break;
            case 36 when age > 8:
                if (Field<bool>(GameAccess.Get(component!, "m_sprPause")!, "m_sprEftFadeOn")) break;
                Point("resume", state!, substate ?? "", component, paused); Next("hover-resume"); break;
            case 37 when age > 3:
                Next("click-resume"); Click(); break;
            case 38 when age > 80:
                if (ultrawide) Require(HelpOptions.Session(game).CanvasWidth == 1720, "resume did not restore 21:9");
                Require(!paused && !Down(8), $"resume failed: paused={paused} A={Down(8)} Start={Down(17)} " +
                    $"fade={Field<bool>(GameAccess.Get(component!, "m_sprPause")!, "m_sprEftFadeOn")} " +
                    $"timer={Field<int>(component!, "m_timer")} connected={Field<bool>(game.GetType().Assembly.GetType("LowLevel.CLASS_GAMEPAD")!.GetField("m_gpad")!.GetValue(null)!, "m_gpadConnected")}");
                Next("mouse-resume-ok"); break;
            case 39 when age > 8:
                Next("escape-pause"); keyboard = new(Keys.Escape); break;
            case 40 when age > 18:
                Require(paused, "Escape did not pause"); Next("pause-ok"); break;
            case 41 when age > 8:
                if (escapePhase == 0) { keyboard = new(Keys.Escape); escapePhase++; age = 0; break; }
                if (escapePhase == 1)
                {
                    Require(state == "GAME", "Escape returned to title instead of resuming");
                    if (paused && age < 120) break; // The original fade advances during Draw.
                    Require(!paused, "Escape did not resume paused gameplay");
                    keyboard = default; escapePhase++; age = 0; break;
                }
                if (escapePhase == 2) { keyboard = new(Keys.Escape); escapePhase++; age = 0; break; }
                keyboard = default;
                Require(paused, "Escape did not pause again after resuming");
                if (Field<bool>(GameAccess.Get(component!, "m_sprPause")!, "m_sprEftFadeOn")) break;
                Console.WriteLine("ESCAPE_TOGGLE_RESULT pause=ok resume=ok pause-again=ok");
                Point("return-title", state!, substate ?? "", component, paused); Next("hover-return-title"); break;
            case 42 when age > 3:
                Next("click-return-title"); Click(); break;
            case 43 when state == "TITLE" && substate == "SELECTION" && age > 8:
                Next("title-return-ok"); break;
            case 44 when age > 4:
                Point("quit", state!, substate!, component, false); Next("hover-quit"); break;
            case 45 when age > 3:
                Next("click-quit"); Click(); break;
        }
    }

    internal void Verify()
    {
        Require(step == 46, $"incomplete step={step}");
        Console.WriteLine("DESKTOP_SCRIPT_RESULT hover=ok click=ok keyboard=ok movement=ok jump=ok focus=ok pause=ok title=ok exit=ok");
    }

    private void DriveOptions(string? state)
    {
        var options = HelpOptions.Session(game);
        optionsAge++;
        if (optionsAge == 3) { keyboard = default; mouse = MouseAt(mouse.X, mouse.Y); }
        if (optionsAge < 8) return;
        keyboard = default;
        mouse = MouseAt(mouse.X, mouse.Y);
        void Advance(params Keys[] keys)
        {
            keyboard = new(keys);
            optionsStep++;
            optionsAge = 0;
        }
        switch (optionsStep)
        {
            case 0:
                if (!optionsEscapeChecked)
                {
                    Require(options.Page == HelpPage.Home, "Help and Options did not open");
                    optionsEscapeChecked = true;
                    keyboard = new(Keys.Escape);
                    optionsAge = 0;
                    break;
                }
                if (!optionsEscapeReopened)
                {
                    Require(!options.IsOpen, "Escape did not close Help and Options");
                    Require(state is "START" or "TITLE", "Escape in Help exited the game");
                    optionsEscapeReopened = true;
                    keyboard = new(Keys.Enter);
                    optionsAge = 0;
                    break;
                }
                Require(options.Page == HelpPage.Home, "Help and Options did not reopen after Escape");
                var title = GameAccess.Get(game, "m_stateComponent")!;
                var background = GameAccess.Get<Texture2D>(GameAccess.Get(title, "m_sprBackground")!, "m_texture");
                var original = GameAccess.Get<ContentManager>(title, "m_content").Load<Texture2D>("TITLE/TITLE_Bg");
                Require(background.Format == original.Format, "Back artwork changed the title background's channel order");
                Require(options.Settings.Mode == DisplayMode.Borderless, "default is not Borderless");
                Advance(Keys.Right); break;
            case 1:
                Require(options.Settings.Mode == DisplayMode.Fullscreen, "Fullscreen was not applied");
                Advance(Keys.Enter); break;
            case 2:
                Require(options.Settings.Mode == DisplayMode.Windowed, "Windowed was not applied");
                Advance(Keys.Right); break;
            case 3:
                Require(options.Settings.Mode == DisplayMode.Borderless, "Borderless was not applied");
                Advance(Keys.Down); break;
            case 4: Advance(Keys.Left); break;
            case 5:
                Require(options.Settings.Resolution == options.Resolutions[^2], "Resolution was not applied");
                Advance(Keys.Right); break;
            case 6:
                Require(options.Settings.Resolution == options.Resolutions[^1], "Resolution was not restored");
                var back = options.Targets()[3];
                mouse = optionsBackAttempt switch
                {
                    0 => MouseAt(back.Center.X, back.Center.Y),
                    1 => MouseAt(1100, 360), // Empty background, outside every target.
                    _ => MouseAt(back.Center.X, back.Bottom + 4) // Visible prompt edge outside the old hitbox.
                };
                Advance(); Click(); break;
            case 7:
                Require(!options.IsOpen, "Mouse Back did not close Help");
                if (++optionsBackAttempt < 3) optionsStep = 5;
                Advance(Keys.Enter); break;
            case 8:
                Require(options.IsOpen, "Help did not reopen");
                Advance(Keys.Back); break;
            case 9:
                Require(!options.IsOpen, "Backspace did not close Help");
                Advance(Keys.Enter); break;
            case 10:
                Require(options.Page == HelpPage.Home, "Help did not reopen");
                var credits = options.Targets()[2];
                mouse = MouseAt(credits.Center.X, credits.Center.Y);
                Advance(); break;
            case 11: Advance(); Click(); break;
            case 12: Advance(); break;
            case 13 when state == "CREDITS":
                if (ultrawide) GameAccess.Set(options, "<Settings>k__BackingField",
                    options.Settings with { Resolution = new Resolution(3440, 1440) });
                Console.WriteLine("OPTIONS_SCRIPT_RESULT display=ok modes=ok resolution=ok mouse-back=ok background-back=ok prompt-edge-back=ok backspace=ok escape=ok credits=ok");
                Next("credits-open"); break;
        }
    }
}
