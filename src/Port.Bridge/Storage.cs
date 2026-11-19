namespace Microsoft.Xna.Framework.Storage;

public sealed class StorageDevice
{
    public bool IsConnected => true;
    public StorageContainer OpenContainer(string name) => new(name);
}

public sealed class StorageContainer(string name) : IDisposable
{
    public static string TitleLocation => AppContext.BaseDirectory;
    public string Path { get; } = CreatePath(name);
    public bool IsDisposed { get; private set; }
    public void Dispose() => IsDisposed = true;

    private static string CreatePath(string name)
    {
        if (name != "Arkedo Series - 03 PIXEL!") throw new ArgumentException("Unknown save container.", nameof(name));
        var overridePath = Environment.GetEnvironmentVariable("PIXEL_PORT_SAVE_DIR");
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var path = string.IsNullOrWhiteSpace(overridePath)
            ? System.IO.Path.Combine(root, "Pixel Recompilation", "Saves")
            : System.IO.Path.GetFullPath(overridePath);
        Directory.CreateDirectory(path);
        return path;
    }
}
