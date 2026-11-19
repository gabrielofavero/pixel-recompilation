using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Port.Hooks;

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

    internal DesktopInputScript(Game game)
    {
        this.game = game;
        DesktopInput.ReadKeyboard = () => keyboard;
        DesktopInput.ReadMouse = () => mouse;
        DesktopInput.IsActive = _ => focused;
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
                Point("title-2", state!, substate!, component, false); Next("hover-credits"); break;
            case 3 when age > 3:
                Require(Field<int>(component!, "m_menuChoix") == 2, "mouse hover did not select Credits");
                Next("click-credits"); Click(); break;
            case 4 when state == "CREDITS" && age > 18:
                Next("credits-open"); break;
            case 5:
                keyboard = new(Keys.Escape);
                if (age > 4) Next("escape-back");
                break;
            case 6 when state == "TITLE" && substate == "SELECTION" && age > 12:
                Next("keyboard-navigation-under-stationary-mouse"); keyboard = new(Keys.W); break;
            case 7 when age > 4:
                Require(Field<int>(component!, "m_menuChoix") == 1, "stationary mouse stole keyboard navigation");
                Next("keyboard-retains-selection"); break;
            case 8 when age > 3:
                Point("title-0", state!, substate!, component, false); Next("hover-new-game"); break;
            case 9 when age > 3:
                Require(Field<int>(component!, "m_menuChoix") == 0, "New Game hover failed");
                Next("click-new-game"); Click(); break;
            case 10 when substate == "CONFIRM" && age > 8:
                Next("confirmation-open"); break;
            case 11 when age > 3:
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
                Point("level-0", state!, substate!, component, false); Next("hover-unlocked-level"); break;
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
                        Point("return-title", state!, substate ?? "", component, true);
                        Click();
                    }
                }
                break;
            case 21 when state == "TITLE" && substate == "SELECTION" && age > 8:
                Next("unlocked-level-click-and-return-title-ok"); break;
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
                Require(!paused && !Down(8), $"resume failed: paused={paused} A={Down(8)} Start={Down(17)} " +
                    $"fade={Field<bool>(GameAccess.Get(component!, "m_sprPause")!, "m_sprEftFadeOn")} " +
                    $"timer={Field<int>(component!, "m_timer")} connected={Field<bool>(game.GetType().Assembly.GetType("LowLevel.CLASS_GAMEPAD")!.GetField("m_gpad")!.GetValue(null)!, "m_gpadConnected")}");
                Next("mouse-resume-ok"); break;
            case 39 when age > 8:
                Next("escape-pause"); keyboard = new(Keys.Escape); break;
            case 40 when age > 18:
                Require(paused, "Escape did not pause"); Next("pause-ok"); break;
            case 41 when age > 8:
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

}
