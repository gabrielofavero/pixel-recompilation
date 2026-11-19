using System.Reflection;
using System.Text.Json;
using Microsoft.Xna.Framework;

internal static class SaveCoverage
{
    public static void Run(Assembly assembly, string action, string directory)
    {
        var path = Path.GetFullPath(directory);
        var userPath = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Pixel Recompilation", "Saves"));
        if (path.Equals(userPath, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(userPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The save probe requires an isolated directory outside the user's saves.");
        Environment.SetEnvironmentVariable("PIXEL_PORT_SAVE_DIR", path);
        var save = assembly.GetType("LowLevel.CLASS_SAVE", true)!;
        object? Call(string name, params object?[] parameters) => save.GetMethod(name)!.Invoke(null, parameters);
        // A fresh process has the original loader's default PlayerIndex.One and unset storage state.
        Call("Ark_SavLoad");
        if (save.GetField("m_initOk")!.GetValue(null) is not true)
            throw new InvalidDataException($"Save initialization failed; reset={save.GetField("m_reset")!.GetValue(null)}.");
        var data = save.GetField("m_saveData")!.GetValue(null)!;
        var type = data.GetType();
        var levels = (bool[])type.GetField("m_savLevels")!.GetValue(data)!;
        var treasures = (bool[][])type.GetField("m_savTresors")!.GetValue(data)!;
        if (action == "write")
        {
            for (int i = 0; i < levels.Length; i++) levels[i] = i < 3;
            for (int i = 0; i < treasures.Length; i++)
                for (int j = 0; j < treasures[i].Length; j++) treasures[i][j] = (i + j) % 2 == 0;
            Call("Ark_SavSave");
        }
        else if (action == "reset")
        {
            Call("Ark_SavReset");
            Call("Ark_SavSave");
        }
        if (!File.Exists(Path.Combine(path, "Player.sav"))) throw new InvalidDataException("Save file is missing from the isolated directory.");
        var names = Enumerable.Range(0, 4).Select(i => (string)Call("NameFromPlayer", (PlayerIndex)i)!).ToArray();
        if (names.Any(name => name != "Player")) throw new InvalidDataException("Unexpected local player save identity.");
        Console.WriteLine("SAVE_PROBE_RESULT " + JsonSerializer.Serialize(new
        {
            action, path, version = type.GetField("m_savVersion")!.GetValue(data), levels, treasures,
            players = names, initialized = true
        }));
    }
}
