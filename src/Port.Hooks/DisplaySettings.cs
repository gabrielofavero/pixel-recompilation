using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Xna.Framework;
using SDL3;

namespace Port.Hooks;

internal enum DisplayMode { Windowed, Borderless, Fullscreen }
internal readonly record struct Resolution(int Width, int Height)
{
    public override string ToString() => $"{Width} x {Height}";
}

internal sealed record DisplaySettings(DisplayMode Mode, Resolution Resolution)
{
    internal static Resolution[] Available(int width, int height)
    {
        Resolution[] candidates = [new(1280, 720), new(1600, 900), new(1920, 1080),
            new(2560, 1440), new(3200, 1800), new(3840, 2160)];
        List<Resolution> choices = [.. candidates];
        if (width / (double)height > 16d / 9)
        {
            choices.AddRange([new(1720, 720), new(2560, 1080), new(3440, 1440), new(3840, 1600),
                new(5120, 1440), new(5120, 2160), new(7680, 2160)]);
            if (height is >= 720 and <= 2160) choices.Add(new(width, height));
        }
        var result = choices.Where(size => size.Width <= width && size.Height <= height).Distinct()
            .OrderBy(size => (long)size.Width * size.Height).ThenBy(size => size.Width).ToArray();
        // Still allow the game to start on screens smaller than 720p.
        if (result.Length == 0)
        {
            int units = Math.Max(1, Math.Min(width / 16, height / 9));
            return [new(units * 16, units * 9)];
        }
        return result;
    }

    internal static Rectangle Fit(int width, int height, int logicalWidth = 1280)
    {
        double scale = Math.Min(width / (double)logicalWidth, height / 720d);
        int w = (int)Math.Round(logicalWidth * scale), h = (int)Math.Round(720 * scale);
        return new((width - w) / 2, (height - h) / 2, w, h);
    }

    internal static Point ToLogical(Point point, Rectangle destination, int logicalWidth = 1280) => new(
        (int)Math.Floor((point.X - destination.X) * (double)logicalWidth / destination.Width - (logicalWidth - 1280) / 2d),
        (int)Math.Floor((point.Y - destination.Y) * 720d / destination.Height));

    internal static string SettingsPath => Path.Combine(
        Environment.GetEnvironmentVariable("PIXEL_PORT_SAVE_DIR") is string root && !string.IsNullOrWhiteSpace(root)
            ? Path.GetFullPath(root)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pixel Recompilation", "Saves"),
        "display-settings.json");

    internal static DisplaySettings Load(Resolution[] available)
    {
        var fallback = new DisplaySettings(DisplayMode.Borderless, available[^1]);
        try
        {
            var loaded = JsonSerializer.Deserialize<DisplaySettings>(File.ReadAllText(SettingsPath));
            return loaded is not null && Enum.IsDefined(loaded.Mode)
                ? loaded with { Resolution = available.Contains(loaded.Resolution) ? loaded.Resolution : available[^1] }
                : fallback;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return fallback;
        }
    }

    internal void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        string temporary = SettingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this));
        File.Move(temporary, SettingsPath, overwrite: true);
    }

    internal static SDL.SDL_DisplayMode Desktop(Game game)
    {
        var mode = SDL.SDL_GetDesktopDisplayMode(SDL.SDL_GetDisplayForWindow(game.Window.Handle));
        if (mode == IntPtr.Zero) throw new InvalidOperationException(SDL.SDL_GetError());
        return Marshal.PtrToStructure<SDL.SDL_DisplayMode>(mode);
    }

    // SDL3-CS exposes only the ref overload; SDL uses NULL to restore desktop borderless mode.
    [DllImport("SDL3", EntryPoint = "SDL_SetWindowFullscreenMode", CallingConvention = CallingConvention.Cdecl)]
    private static extern byte ClearFullscreenMode(IntPtr window, IntPtr mode);

    internal bool Apply(Game game)
    {
        var graphics = GameAccess.Get<GraphicsDeviceManager>(game, "m_graphics");
        var window = game.Window.Handle;
        if (!SDL.SDL_SetWindowFullscreen(window, false)) throw new InvalidOperationException(SDL.SDL_GetError());
        if (Mode == DisplayMode.Fullscreen)
        {
            var desktop = Desktop(game);
            if (SDL.SDL_GetClosestFullscreenDisplayMode(SDL.SDL_GetDisplayForWindow(window), Resolution.Width, Resolution.Height,
                desktop.refresh_rate, true, out var closest) && closest.w == Resolution.Width && closest.h == Resolution.Height)
                desktop = closest;
            if (!SDL.SDL_SetWindowFullscreenMode(window, ref desktop)) throw new InvalidOperationException(SDL.SDL_GetError());
        }
        else if (ClearFullscreenMode(window, IntPtr.Zero) == 0)
            throw new InvalidOperationException(SDL.SDL_GetError());
        graphics.PreferredBackBufferWidth = Resolution.Width;
        graphics.PreferredBackBufferHeight = Resolution.Height;
        graphics.IsFullScreen = Mode != DisplayMode.Windowed;
        game.Window.IsBorderlessEXT = Mode == DisplayMode.Borderless;
        graphics.ApplyChanges();
        // ApplyChanges may skip a reset when only the SDL fullscreen mode changed.
        bool exclusive = true;
        if (Mode != DisplayMode.Windowed && !SDL.SDL_SetWindowFullscreen(window, true))
        {
            if (Mode != DisplayMode.Fullscreen) throw new InvalidOperationException(SDL.SDL_GetError());
            // A driver can advertise modes it cannot switch to (for example a remote
            // desktop). Keep fullscreen usable at the desktop mode in that case.
            Console.WriteLine($"DISPLAY_DESKTOP_FALLBACK {SDL.SDL_GetError()}");
            if (ClearFullscreenMode(window, IntPtr.Zero) == 0 || !SDL.SDL_SetWindowFullscreen(window, true))
                throw new InvalidOperationException(SDL.SDL_GetError());
            exclusive = false;
        }
        SDL.SDL_SyncWindow(window);
        return exclusive;
    }
}
