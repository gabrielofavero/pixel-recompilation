using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Runtime.InteropServices;

int? exitAfterFrames = null;
string? contentDirectory = null;
if (args.Length != 0)
{
    if (args.Length is not (2 or 4) || args[0] != "--frames" || !int.TryParse(args[1], out var frames) || frames < 1 ||
        (args.Length == 4 && args[2] != "--content"))
    {
        Console.Error.WriteLine("Usage: Port.Smoke [--frames <positive count> [--content <decoded-content-directory>]]");
        return 2;
    }
    exitAfterFrames = frames;
    if (args.Length == 4) contentDirectory = Path.GetFullPath(args[3]);
}

foreach (var file in new[] { "SDL3.dll", "FNA3D.dll", "FAudio.dll", "libtheorafile.dll", "D3D12/D3D12Core.dll" })
{
    var path = Path.Combine(AppContext.BaseDirectory, file);
    if (!File.Exists(path) || !NativeLibrary.TryLoad(path, out var handle))
    {
        Console.Error.WriteLine($"SMOKE_FAIL native dependency unavailable: {file} ({path})");
        return 1;
    }
    NativeLibrary.Free(handle);
}

try
{
    using var game = new SmokeGame(exitAfterFrames, contentDirectory);
    game.Run();
    Console.WriteLine($"SMOKE_OK frames={game.DrawnFrames}");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine($"SMOKE_FAIL {error}");
    if (IsNativeLoadFailure(error))
        Console.Error.WriteLine("Native-load failure: check the pinned win-x64 DLLs beside Port.Smoke.exe.");
    return 1;
}

static bool IsNativeLoadFailure(Exception error) =>
    error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException ||
    (error.InnerException is not null && IsNativeLoadFailure(error.InnerException));

internal sealed class SmokeGame : Game
{
    private readonly GraphicsDeviceManager graphics;
    private readonly int? exitAfterFrames;
    private readonly string? contentDirectory;
    public int DrawnFrames { get; private set; }

    public SmokeGame(int? exitAfterFrames, string? contentDirectory)
    {
        this.exitAfterFrames = exitAfterFrames;
        this.contentDirectory = contentDirectory;
        graphics = new GraphicsDeviceManager(this) { PreferredBackBufferWidth = 640, PreferredBackBufferHeight = 360 };
        IsMouseVisible = false;
        Window.Title = "PIXEL! FNA smoke test";
    }

    protected override void LoadContent()
    {
        if (contentDirectory is not null)
        {
            Content.RootDirectory = contentDirectory;
            using var texture = Content.Load<Texture2D>("TITLE/TITLE_Bg");
            Console.WriteLine($"CONTENT_OK TITLE/TITLE_Bg {texture.Width}x{texture.Height}");
        }
        base.LoadContent();
    }

    protected override void Initialize()
    {
        base.Initialize();
        Console.WriteLine($"SMOKE_WINDOW {GraphicsDevice.PresentationParameters.BackBufferWidth}x{GraphicsDevice.PresentationParameters.BackBufferHeight}");
    }

    protected override void Update(GameTime gameTime)
    {
        if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed ||
            (exitAfterFrames is int limit && DrawnFrames >= limit))
            Exit();
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.CornflowerBlue);
        DrawnFrames++;
        if (DrawnFrames == 1) Console.WriteLine("SMOKE_FRAME cleared");
        base.Draw(gameTime);
    }
}
