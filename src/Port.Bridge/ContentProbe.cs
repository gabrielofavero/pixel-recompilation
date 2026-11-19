using Microsoft.Xna.Framework.Content;

namespace Port.Bridge;

public static class ContentProbe
{
    public static T Load<T>(ContentManager manager, string asset)
    {
        try
        {
            var value = manager.Load<T>(asset);
            if (Environment.GetEnvironmentVariable("PIXEL_PORT_TRACE_CONTENT") == "1")
                Console.WriteLine($"CONTENT_LOAD_OK asset={asset} type={typeof(T).FullName}");
            return value;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"CONTENT_LOAD_FAIL asset={asset} type={typeof(T).FullName}: {error.Message}");
            throw;
        }
    }
}
