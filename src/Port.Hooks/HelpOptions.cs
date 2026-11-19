using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Content;
using SDL3;

namespace Port.Hooks;

// Authored menus and presentation around the original logical game coordinates.
public static class HelpOptions
{
    public const string PatchId = "help-options-v1";
    private static readonly ConditionalWeakTable<Game, OptionsSession> Sessions = new();
    internal static Action<Game, Texture2D>? CaptureFrame { get; set; }
    internal static OptionsSession Session(Game game) => Sessions.GetValue(game, key => new(key));

    public static void Initialize(object game) => Session((Game)game).Initialize();
    public static void BeforeDraw(object game) => Session((Game)game).BeforeDraw();
    public static void AfterDraw(object game) => Session((Game)game).AfterDraw();
    public static bool Selection(object title) => Session(((GameComponent)title).Game).Selection(title);
    public static bool TitleDisplay(object title) => Session(((GameComponent)title).Game).TitleDisplay(title);
}

internal enum HelpPage { Closed, Home }

internal sealed class OptionsSession(Game game)
{
    internal static bool Pressed(int state) => (state & 1) != 0;
    internal HelpPage Page { get; private set; }
    internal int Selected { get; private set; }
    internal DisplaySettings Settings { get; private set; } = null!;
    internal Resolution[] Resolutions { get; private set; } = [];
    private RenderTarget2D? canvas;
    private readonly Dictionary<int, RenderTarget2D> canvases = [];
    private RenderTarget2D? transition;
    private int transitionFrames;
    private object? lastComponent;
    internal int CanvasWidth { get; private set; } = 1280;
    internal static Func<Game, Point> OutputSize { get; set; } = game =>
    {
        SDL.SDL_GetWindowSizeInPixels(game.Window.Handle, out int width, out int height);
        return new(width, height);
    };
    private SpriteBatch batch = null!;
    private SpriteFont? font;
    private MenuArtwork artwork = null!;
    private readonly PromptArtwork prompts = new();
    private Texture2D pixel = null!;
    private MouseState previousMouse;
    private string message = "";
    private bool returnFromCredits, sawCredits, wasActive, waitForRelease;
    private Rectangle destination;
    private float opacity = 1;
    internal bool IsOpen => Page != HelpPage.Closed;

    internal void Initialize()
    {
        RefreshResolutions();
        Settings = DisplaySettings.Load(Resolutions);
        try { Settings.Apply(game); }
        catch (Exception error) when (error is InvalidOperationException or Microsoft.Xna.Framework.Graphics.NoSuitableGraphicsDeviceException)
        {
            Console.Error.WriteLine($"DISPLAY_FALLBACK {error.Message}");
            Settings = new(DisplayMode.Borderless, Resolutions[0]);
            Settings.Apply(game);
        }
        batch = new SpriteBatch(game.GraphicsDevice);
        artwork = new MenuArtwork(game.GraphicsDevice);
        pixel = new Texture2D(game.GraphicsDevice, 1, 1);
        pixel.SetData(new[] { Color.White });
        game.Disposed += (_, _) => { foreach (var target in canvases.Values) target.Dispose(); artwork.Dispose(); batch.Dispose(); pixel.Dispose(); };
    }

    private void RefreshResolutions()
    {
        var desktop = DisplaySettings.Desktop(game);
        Resolutions = DisplaySettings.Available(desktop.w, desktop.h);
    }

    internal Rectangle Destination(int logicalWidth = 1280)
    {
        var pp = game.GraphicsDevice.PresentationParameters;
        // FNA scales the backbuffer to the SDL window. Compensate for that scaling
        // so desktop fullscreen keeps the correct aspect even on ultrawide screens.
        var output = OutputSize(game);
        int width = output.X, height = output.Y;
        if (width <= 0 || height <= 0) return DisplaySettings.Fit(pp.BackBufferWidth, pp.BackBufferHeight, logicalWidth);
        var fit = DisplaySettings.Fit(width, height, logicalWidth);
        return new((int)Math.Round(fit.X * (double)pp.BackBufferWidth / width),
            (int)Math.Round(fit.Y * (double)pp.BackBufferHeight / height),
            (int)Math.Round(fit.Width * (double)pp.BackBufferWidth / width),
            (int)Math.Round(fit.Height * (double)pp.BackBufferHeight / height));
    }

    internal MouseState LogicalMouse(MouseState mouse)
    {
        var point = DisplaySettings.ToLogical(new(mouse.X, mouse.Y), Destination(CanvasWidth), CanvasWidth);
        return new(point.X, point.Y, mouse.ScrollWheelValue, mouse.LeftButton, mouse.MiddleButton,
            mouse.RightButton, mouse.XButton1, mouse.XButton2);
    }

    internal void BeforeDraw()
    {
        if (returnFromCredits && GameAccess.Get(game, "m_state")?.ToString() == "CREDITS") sawCredits = true;
        var component = GameAccess.Get(game, "m_stateComponent");
        bool keyboard = DesktopInput.KeyboardHints(game);
        prompts.Update(game.Content, keyboard);
        if (component?.GetType().GetField("m_content", System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(component) is ContentManager content)
            prompts.Update(content, keyboard);
        object? level = null;
        int width = 1280;
        if (component?.GetType().FullName == "Scale.GAME.GAMEMain" && GameAccess.Get(component, "m_level") is object loaded)
        {
            level = loaded;
            if (Ultrawide.Expand(GameAccess.Get(level, "m_state")!.ToString()!, GameAccess.Get<int>(component, "m_timerIntro")))
            {
                width = Ultrawide.WidthFor(Settings.Resolution.Width, Settings.Resolution.Height);
            }
            prompts.Update(GameAccess.Get<ContentManager>(level, "Content"), keyboard);
            TutorialHints.Update(level, keyboard);
        }
        if (canvas is not null && !canvas.IsDisposed && canvas.Width != width && ReferenceEquals(lastComponent, component) && level is not null)
        {
            transition = canvas;
            transitionFrames = 12;
        }
        else if (!ReferenceEquals(lastComponent, component)) { transition = null; transitionFrames = 0; }
        lastComponent = component;
        CanvasWidth = width;
        if (!canvases.TryGetValue(width, out canvas) || canvas.IsDisposed)
            canvases[width] = canvas = new RenderTarget2D(game.GraphicsDevice, width, 720, false, SurfaceFormat.Color, DepthFormat.None,
                0, RenderTargetUsage.PreserveContents);
        destination = Destination(width);
        Ultrawide.Begin(game, level, width);
        game.GraphicsDevice.SetRenderTarget(canvas);
    }

    internal void AfterDraw()
    {
        Ultrawide.End();
        var component = GameAccess.Get(game, "m_stateComponent");
        if (component?.GetType().FullName == "Scale.TITLE.TITLEMain")
        {
            var fade = game.GetType().Assembly.GetType("LowLevel.CLASS_MOD_FADE")!.GetField("m_fadeCurrent")!.GetValue(null)!;
            opacity = 1 - GameAccess.Get<float>(fade, "m_fadeAlpha") / 255f;
            font = GameAccess.Get<SpriteFont>(GameAccess.Get(component, "m_fntPopup")!, "m_fntFont");
            batch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
            if (IsOpen && message.Length != 0) Text(message, 640, 680, 18, Color.White, true);
            batch.End();
        }
        game.GraphicsDevice.SetRenderTarget(null);
        HelpOptions.CaptureFrame?.Invoke(game, canvas!);
        game.GraphicsDevice.Clear(Color.Black);
        batch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
        batch.Draw(canvas, destination, Color.White);
        batch.End();
        if (transition is not null && transitionFrames > 0 && !transition.IsDisposed)
        {
            var bounds = Destination(transition.Width);
            float alpha = transitionFrames-- / 12f;
            var pp = game.GraphicsDevice.PresentationParameters;
            batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
            batch.Draw(pixel, new Rectangle(0, 0, pp.BackBufferWidth, bounds.Top), Color.Black * alpha);
            batch.Draw(pixel, new Rectangle(0, bounds.Bottom, pp.BackBufferWidth, pp.BackBufferHeight - bounds.Bottom), Color.Black * alpha);
            batch.Draw(pixel, new Rectangle(0, bounds.Top, bounds.Left, bounds.Height), Color.Black * alpha);
            batch.Draw(pixel, new Rectangle(bounds.Right, bounds.Top, pp.BackBufferWidth - bounds.Right, bounds.Height), Color.Black * alpha);
            batch.Draw(transition, bounds, Color.White * alpha);
            batch.End();
        }
    }

    internal bool TitleDisplay(object title)
    {
        if (returnFromCredits && sawCredits)
        {
            returnFromCredits = false;
            sawCredits = false;
            Open(HelpPage.Home, 2);
        }
        font = GameAccess.Get<SpriteFont>(GameAccess.Get(title, "m_fntPopup")!, "m_fntFont");
        var content = GameAccess.Get<ContentManager>(title, "m_content");
        artwork.Background(GameAccess.Get(title, "m_sprBackground")!, content, IsOpen, prompts);
        var sprites = GameAccess.Get<object[]>(title, "m_sprMenuChoix");
        string[] labels = IsOpen
            ? [$"Display: {Settings.Mode}", $"Resolution: {Settings.Resolution}", "Credits"]
            : ["New Game", "Continue", "Help and Options"];
        for (int i = 0; i < 3; i++) artwork.Label(sprites[i], font, labels[i], content);
        int selected = IsOpen ? Selected : GameAccess.Get<int>(title, "m_menuChoix");
        var cursor = GameAccess.Get(title, "m_sprCursor")!;
        string selectedLabel = labels[Math.Min(selected, 2)];
        GameAccess.Set(cursor, "m_sprX", MenuArtwork.OriginalAsset(selectedLabel) is not null ? 495f
            : 645f - artwork.LabelWidth(font, selectedLabel) / 2 - 55);
        if (!IsOpen) return false;
        GameAccess.Set(cursor, "m_sprY", 483f + Selected * 50);
        Submit(GameAccess.Get(title, "m_sprBackground")!);
        Submit(GameAccess.Get(title, "m_sprLogo")!);
        for (int i = 0; i < 3; i++)
        {
            GameAccess.Set(sprites[i], "m_sprFrmCur", i == Selected ? 0 : 1);
            Submit(sprites[i]);
        }
        Submit(cursor);
        return true;
    }

    private static void Submit(object sprite) => sprite.GetType().GetMethod("Ark_SprDisplayToZSort")!.Invoke(sprite, null);

    private void Open(HelpPage page, int selected = 0)
    {
        Page = page;
        Selected = selected;
        message = "";
        if (page == HelpPage.Home) RefreshResolutions();
    }

    internal bool Selection(object title)
    {
        var padType = game.GetType().Assembly.GetType("LowLevel.CLASS_GAMEPAD")!;
        var pad = padType.GetField("m_gpad")!.GetValue(null)!;
        var buttons = GameAccess.Get<Array>(pad, "m_gpadButtons");
        bool Press(int slot) => Pressed(Convert.ToInt32(buttons.GetValue(slot)));
        bool down = Press(3), up = Press(2), confirm = Press(8) || Press(17), back = Press(9) || Press(18);
        var mouse = LogicalMouse(DesktopInput.ReadMouse());
        bool active = DesktopInput.IsActive(game);
        if (!active || !wasActive) waitForRelease = true;
        wasActive = active;
        var physical = GamePad.GetState(GameAccess.Get<PlayerIndex>(pad, "m_gpadPlayerIndex"));
        bool padHeld = physical.Buttons != default || physical.DPad != default || physical.ThumbSticks.Left.LengthSquared() > 0.04f;
        if (active && DesktopInput.ReadKeyboard().GetPressedKeys().Length == 0 && mouse.LeftButton == ButtonState.Released && !padHeld)
            waitForRelease = false;
        bool click = mouse.LeftButton == ButtonState.Pressed && previousMouse.LeftButton == ButtonState.Released;
        bool moved = mouse.X != previousMouse.X || mouse.Y != previousMouse.Y;
        previousMouse = mouse;
        if (!active || waitForRelease) return IsOpen;
        if (!IsOpen)
        {
            if (GameAccess.Get<int>(title, "m_menuChoix") != 2 || !confirm) return false;
            Open(HelpPage.Home);
            return true;
        }
        game.IsMouseVisible = true;
        var rows = Targets();
        int hover = Array.FindIndex(rows, row => row.Contains(mouse.X, mouse.Y));
        if (moved || click)
        {
            if (hover is >= 0 and < 3) Selected = hover;
        }
        if (down) Selected = (Selected + 1) % 3;
        else if (up) Selected = (Selected + 2) % 3;
        // Option labels keep their click action; every other left-click is Back.
        if (back || click && hover is not (>= 0 and < 3))
        {
            Open(HelpPage.Closed);
            GameAccess.Select(title, 2);
            return true;
        }
        int direction = Press(0) ? -1 : Press(1) ? 1 : 0;
        if (Environment.GetEnvironmentVariable("PIXEL_PORT_TRACE_INPUT") == "1" && (up || down || confirm || click || direction != 0))
            Console.WriteLine($"OPTIONS_INPUT page={Page} selected={Selected} direction={direction} confirm={confirm} click={click} mode={Settings.Mode}");
        if (confirm || click) direction = 1;
        if (direction == 0) return true;
        if (Selected == 2)
        {
            if (!(confirm || click)) return true;
            Page = HelpPage.Closed;
            returnFromCredits = true;
            GameAccess.Select(title, 2);
            // The original Credits branch owns its fade, music and state transition.
            var buttonType = buttons.GetType().GetElementType()!;
            buttons.SetValue(Enum.ToObject(buttonType, 0), 2);
            buttons.SetValue(Enum.ToObject(buttonType, 0), 3);
            buttons.SetValue(Enum.ToObject(buttonType, 3), 8);
            return false;
        }
        if (Selected == 0)
            Apply(Settings with { Mode = (DisplayMode)(((int)Settings.Mode + direction + 3) % 3) });
        else
        {
            int index = Array.IndexOf(Resolutions, Settings.Resolution);
            Apply(Settings with { Resolution = Resolutions[(index + direction + Resolutions.Length) % Resolutions.Length] });
        }
        return true;
    }

    internal void Apply(DisplaySettings next)
    {
        try
        {
            bool exclusive = next.Apply(game);
            Settings = next;
            transition = null;
            transitionFrames = 0;
            message = exclusive ? "" : "Fullscreen uses desktop mode on this display.";
            try { Settings.Save(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { message = "Settings applied, but could not be saved."; }
        }
        catch (Exception error) when (error is InvalidOperationException or NoSuitableGraphicsDeviceException)
        {
            Console.Error.WriteLine($"DISPLAY_CHANGE_FAILED {error.Message}");
            Settings.Apply(game);
            message = "Display change unavailable. Previous settings restored.";
        }
        waitForRelease = true;
    }

    internal Rectangle[] Targets() => [new(400, 462, 490, 42), new(400, 512, 490, 42),
        new(400, 562, 490, 42), new(60, 590, 210, 64)];

    private float Scale(int height) => height / font!.MeasureString("M").Y;
    private void Text(string value, int x, int y, int height, Color color, bool center = false, bool right = false)
    {
        float scale = Scale(height);
        var size = font!.MeasureString(value) * scale;
        var position = new Vector2(x - (center ? size.X / 2 : right ? size.X : 0), y - (center ? size.Y / 2 : 0));
        batch.DrawString(font, value, position + new Vector2(2, 2), Color.Black * opacity, 0, Vector2.Zero, scale, SpriteEffects.None, 0);
        batch.DrawString(font, value, position, color * opacity, 0, Vector2.Zero, scale, SpriteEffects.None, 0);
    }
}
