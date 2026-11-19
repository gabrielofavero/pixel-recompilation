using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Port.Hooks;

public static class DesktopInput
{
    public const string PatchId = "desktop-input-v1";
    private static readonly ConditionalWeakTable<Game, InputSession> Sessions = new();
    [ThreadStatic] private static InputSession? current;
    internal static Func<KeyboardState> ReadKeyboard { get; set; } = Keyboard.GetState;
    internal static Func<MouseState> ReadMouse { get; set; } = Mouse.GetState;
    internal static Func<Game, bool> IsActive { get; set; } = game => game.IsActive;
    internal static bool Enabled => Environment.GetEnvironmentVariable("PIXEL_PORT_DISABLE_DESKTOP_INPUT") != "1";
    internal static bool KeyboardHints(Game game) => Enabled && Sessions.TryGetValue(game, out var session) && session.DesktopOwned;

    public static void BeforeUpdate(object pad)
    {
        if (!Enabled) return;
        var game = ((GameComponent)pad).Game;
        current = Sessions.GetValue(game, key => new InputSession(key));
        current.Sample(pad, ReadKeyboard(), ReadMouse(), IsActive(game));
    }

    public static KeyboardState GetKeyboardState() => Enabled ? current?.Mapped ?? default : Keyboard.GetState();

    public static void AfterUpdate(object pad)
    {
        if (Enabled && current?.DesktopOwned == true)
            GameAccess.Set(pad, "m_gpadConnected", current.Active);
    }
}

internal sealed class InputSession(Game game)
{
    private KeyboardState previousKeyboard;
    private readonly HashSet<Keys> blockedKeys = [];
    private MouseState previousMouse;
    private GamePadState previousPad;
    private bool sampled, waitForRelease, blockedLeftButton;
    private string lastContext = "";
    private object? previousComponent;
    private string state = "", substate = "";
    private bool paused;
    internal KeyboardState Mapped { get; private set; }
    internal bool DesktopOwned { get; private set; }
    internal bool Active { get; private set; }

    internal void Sample(object pad, KeyboardState keyboard, MouseState mouse, bool active)
    {
        var options = HelpOptions.Session(game);
        mouse = options.LogicalMouse(mouse);
        Active = active;
        state = GameAccess.Get(game, "m_state")?.ToString() ?? "";
        var component = GameAccess.Get(game, "m_stateComponent");
        substate = component is null ? "" : ReadSubstate(component);
        paused = state == "GAME" && component is not null && GameAccess.Get(component, "m_level") is object level &&
            GameAccess.Get<bool>(level, "m_pause") && GameAccess.Get<int>(component, "m_timerIntro") == 0;
        string context = $"{state}/{substate}/{paused}/{options.Page}";
        bool changedContext = context != lastContext || !ReferenceEquals(component, previousComponent);
        if (changedContext && sampled)
        {
            foreach (var key in previousKeyboard.GetPressedKeys().Where(keyboard.IsKeyDown)) blockedKeys.Add(key);
            if (previousMouse.LeftButton == ButtonState.Pressed && mouse.LeftButton == ButtonState.Pressed)
                blockedLeftButton = true;
        }
        if (mouse.LeftButton == ButtonState.Released) blockedLeftButton = false;
        blockedKeys.RemoveWhere(keyboard.IsKeyUp);
        var targets = options.IsOpen ? [] : MenuTargets.Build(state, substate, component, paused);
        game.IsMouseVisible = options.IsOpen || targets.Count > 0;

        bool click = mouse.LeftButton == ButtonState.Pressed && previousMouse.LeftButton == ButtonState.Released;
        bool moved = sampled && (mouse.X != previousMouse.X || mouse.Y != previousMouse.Y);
        if (!active) waitForRelease = true;
        if (waitForRelease && active && keyboard.GetPressedKeys().Length == 0 && mouse.LeftButton == ButtonState.Released)
            waitForRelease = false;
        var filtered = new KeyboardState(keyboard.GetPressedKeys().Where(key => !blockedKeys.Contains(key)).ToArray());
        Mapped = active && !waitForRelease ? KeyboardScheme.Translate(filtered, state == "GAME", paused, substate == "START") : default;
        if (active && !waitForRelease && !changedContext && !blockedLeftButton && state == "GAME" && !paused &&
            mouse.LeftButton == ButtonState.Pressed)
            Mapped = new KeyboardState(Mapped.GetPressedKeys().Append(Keys.B).Distinct().ToArray());

        var selected = GameAccess.Get<PlayerIndex>(pad, "m_gpadPlayerIndex");
        var physical = GamePad.GetState(selected);
        bool padActivity = physical.IsConnected && (physical.Buttons != default || physical.DPad != default ||
            physical.ThumbSticks.Left.LengthSquared() > 0.04f || physical.ThumbSticks.Right.LengthSquared() > 0.04f ||
            physical.Triggers.Left > 0.1f || physical.Triggers.Right > 0.1f);
        // Prefer a newly pressed device over a button/stick still held on the
        // other one. Hint switching must work while a sign is already open.
        bool keyboardActivity = active && !waitForRelease && keyboard.GetPressedKeys().Any(previousKeyboard.IsKeyUp);
        bool padChanged = physical.Buttons != previousPad.Buttons || physical.DPad != previousPad.DPad ||
            physical.ThumbSticks != previousPad.ThumbSticks || physical.Triggers != previousPad.Triggers;
        if (active && padActivity && padChanged) DesktopOwned = false;
        if (keyboardActivity || active && !waitForRelease && !blockedLeftButton && click) DesktopOwned = true;

        if (active && !waitForRelease && !changedContext && (moved || click))
        {
            var hovered = targets.FirstOrDefault(target => target.Bounds.Contains(mouse.X, mouse.Y));
            if (hovered is not null)
            {
                if (hovered.Selection is int index && GameAccess.Get<int>(component!, "m_menuChoix") != index)
                    GameAccess.Select(component!, index);
                if (click)
                {
                    Mapped = new KeyboardState(Mapped.GetPressedKeys().Append(hovered.Action).Distinct().ToArray());
                    DesktopOwned = true;
                    if (Environment.GetEnvironmentVariable("PIXEL_PORT_TRACE_INPUT") == "1")
                        Console.WriteLine($"DESKTOP_CLICK target={hovered.Id} state={state}");
                }
            }
        }
        previousKeyboard = keyboard;
        previousMouse = mouse;
        previousPad = physical;
        sampled = true;
        lastContext = context;
        previousComponent = component;
    }

    private static string ReadSubstate(object component)
    {
        string name = component.GetType().FullName == "Scale.TITLE.TITLEMain" ? "m_menuState" : "m_state";
        return component.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic)?.GetValue(component)?.ToString() ?? "";
    }
}
