using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Storage;

namespace Port.Bridge;

public static class SaveStore
{
    public static void Create<T>(StorageDevice device, PlayerIndex player) where T : class, new()
    {
        if (device is null || !device.IsConnected) return;
        using var container = device.OpenContainer("Arkedo Series - 03 PIXEL!");
        if (!File.Exists(SavePath(container, player))) Save(device, new T(), player);
    }

    public static T? Load<T>(StorageDevice device, PlayerIndex player) where T : class
    {
        if (device is null || !device.IsConnected) return null;
        using var container = device.OpenContainer("Arkedo Series - 03 PIXEL!");
        var path = SavePath(container, player);
        try
        {
            using var stream = File.OpenRead(path);
            return Deserialize<T>(stream);
        }
        catch (FileNotFoundException)
        {
            Console.WriteLine($"SAVE_MISSING path={path}");
            return null;
        }
        catch (Exception error) when (error is XmlException or InvalidOperationException or InvalidDataException)
        {
            // The read stream has been disposed before moving the damaged file on Windows.
            var backup = path + ".corrupt-" + Guid.NewGuid().ToString("N");
            File.Move(path, backup);
            Console.Error.WriteLine($"SAVE_CORRUPT preserved={backup} reason={error.GetType().Name}; creating fresh progress");
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"SAVE_LOAD_FAILED path={path} reason={error.Message}");
            throw;
        }
    }

    public static void Save<T>(StorageDevice device, T data, PlayerIndex player) where T : class
    {
        if (device is null || !device.IsConnected) return;
        using var container = device.OpenContainer("Arkedo Series - 03 PIXEL!");
        var path = SavePath(container, player);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            Validate(data);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                new XmlSerializer(typeof(T)).Serialize(stream, data);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
            Console.WriteLine($"SAVE_WRITE_OK path={path}");
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"SAVE_WRITE_FAILED path={path} reason={error.Message}");
            throw;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string SavePath(StorageContainer container, PlayerIndex player)
    {
        var gamer = Gamer.SignedInGamers.Single(gamer => gamer.PlayerIndex == player);
        return Path.Combine(container.Path, gamer.Gamertag + ".sav");
    }

    private static T Deserialize<T>(Stream stream) where T : class
    {
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 65536
        });
        var document = XDocument.Load(reader);
        var root = document.Root;
        if (root?.Name != "CLASS_SAVEDATA" ||
            new[] { "m_savVersion", "m_savLevels", "m_savTresors" }.Any(name => root.Elements(name).Count() != 1))
            throw new InvalidDataException("Save XML is missing required fields.");
        using var serialized = document.CreateReader();
        var data = new XmlSerializer(typeof(T)).Deserialize(serialized) as T
            ?? throw new InvalidDataException("Save XML has no data.");
        Validate(data);
        return data;
    }

    private static void Validate<T>(T data) where T : class
    {
        var type = typeof(T);
        if (data is null || type.GetField("m_savVersion")?.GetValue(data) is not 1 ||
            type.GetField("m_savLevels")?.GetValue(data) is not bool[] { Length: 7 } ||
            type.GetField("m_savTresors")?.GetValue(data) is not bool[][] { Length: 6 } treasures ||
            treasures.Any(row => row is not { Length: 3 }))
            throw new InvalidDataException("Unsupported save version or invalid progress dimensions.");
    }
}
