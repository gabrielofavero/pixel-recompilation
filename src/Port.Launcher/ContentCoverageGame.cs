using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

internal sealed class ContentCoverageGame : Game
{
    private readonly string root;
    private readonly GraphicsDeviceManager graphics;

    public ContentCoverageGame(string root)
    {
        this.root = root;
        graphics = new GraphicsDeviceManager(this) { PreferredBackBufferWidth = 640, PreferredBackBufferHeight = 360 };
        Window.Title = "PIXEL! content coverage";
    }

    protected override void LoadContent()
    {
        int count = 0, custom = 0;
        foreach (var path in Directory.GetFiles(root, "*.xnb", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            using var manager = new ContentManager(Services, root);
            var asset = Path.ChangeExtension(Path.GetRelativePath(root, path), null).Replace('\\', '/');
            var value = Port.Bridge.ContentProbe.Load<object>(manager, asset);
            if (value.GetType().Assembly.GetName().Name == "Scale_Shared_ContentGame") custom++;
            Console.WriteLine($"CONTENT_ASSET_OK asset={asset} type={value.GetType().FullName}");
            count++;
        }
        if (count != 340 || custom != 69)
            throw new InvalidDataException($"Unexpected content totals: assets={count}, custom={custom}.");
        Console.WriteLine($"CONTENT_COVERAGE_OK assets={count} custom={custom}");
        base.LoadContent();
    }

    protected override void Update(GameTime gameTime) => Exit();
}
