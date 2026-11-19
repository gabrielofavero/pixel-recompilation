using System.Security.Cryptography;
using System.Text.Json;

namespace Port.Prepare;

public sealed record PreparationOptions(string PackagePath, string OutputDirectory, string PrivateRoot,
    string SupportedVersionsPath, string PatchAnchorsPath, string FnaPath, string BridgePath, string? AudioDecoderDirectory = null,
    Action<string, int, int>? Progress = null);
public sealed record PreparationResult(string OutputDirectory, int Files, int Xnbs, string ManifestSha256);

internal sealed record SupportedVersion(string Title, string TitleId, int PackageBytes, string PackageSha256,
    int FileTableBlocks, int DataBlocks, int Files, string AssemblyPath, string AssemblySha256,
    string AssemblyMvid, string ContentAssemblyPath, string ContentAssemblySha256, string ContentAssemblyMvid);

public static class PreparationEngine
{
    public const string Version = "0.7.0";

    public static void ValidatePackage(string packagePath, string supportedVersionsPath) =>
        StfsPackage.Read(packagePath, ReadVersion(supportedVersionsPath));

    public static PreparationResult Prepare(PreparationOptions options)
    {
        var privateRoot = Path.GetFullPath(options.PrivateRoot);
        var destination = Path.GetFullPath(options.OutputDirectory);
        if (!destination.StartsWith(privateRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) || destination == privateRoot)
            throw new ArgumentException("Preparation output must be a new directory beneath the private root.");
        if (Path.Exists(destination)) throw new IOException($"Preparation target already exists: {destination}");
        ValidateParents(privateRoot, destination);
        var version = ReadVersion(options.SupportedVersionsPath);
        options.Progress?.Invoke("extract", 0, 1);
        var files = StfsPackage.Read(options.PackagePath, version);
        using var dependencies = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(options.SupportedVersionsPath))!, "dependencies.json")));
        var audioDecoder = dependencies.RootElement.GetProperty("audioDecoder");
        var decoderDirectory = Path.GetFullPath(options.AudioDecoderDirectory ?? Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(options.SupportedVersionsPath))!, "..", "out", "deps",
            "vgmstream-" + audioDecoder.GetProperty("version").GetString()));
        using var anchorDocument = JsonDocument.Parse(File.ReadAllBytes(options.PatchAnchorsPath));
        var anchors = anchorDocument.RootElement;
        if (anchors.GetProperty("schemaVersion").GetInt32() != 1)
            throw new InvalidDataException("Unsupported patch anchor schema.");
        var assemblies = anchors.GetProperty("assemblies").EnumerateArray().ToArray();
        if (assemblies.Length != 2) throw new InvalidDataException("Expected two guarded game assemblies.");
        var fnaPath = Path.GetFullPath(options.FnaPath);
        var bridgePath = Path.GetFullPath(options.BridgePath);
        if (Path.GetFileName(fnaPath) != "FNA.dll" || Path.GetFileName(bridgePath) != "Port.Bridge.dll")
            throw new InvalidDataException("Unexpected preparation dependency identity.");
        var fnaConfigPath = fnaPath + ".config";
        var fnaBytes = File.ReadAllBytes(fnaPath);
        var fnaConfigBytes = File.ReadAllBytes(fnaConfigPath);
        var bridgeBytes = File.ReadAllBytes(bridgePath);
        var hooksBytes = File.ReadAllBytes(typeof(Port.Hooks.DesktopInput).Assembly.Location);
        var decoded = new XnbDecoder(fnaPath);
        var outputs = new List<(string Path, byte[] Original, byte[] Run)>();
        int xnbCount = 0;
        foreach (var file in files)
        {
            options.Progress?.Invoke("content", outputs.Count, files.Count);
            byte[] run = file.Bytes;
            if (file.RelativePath.EndsWith(".xnb", StringComparison.OrdinalIgnoreCase))
            {
                run = decoded.Decode(file.Bytes, file.RelativePath);
                xnbCount++;
            }
            else if (file.RelativePath == version.AssemblyPath || file.RelativePath == version.ContentAssemblyPath)
            {
                bool main = file.RelativePath == version.AssemblyPath;
                var expectedHash = main ? version.AssemblySha256 : version.ContentAssemblySha256;
                var expectedMvid = main ? version.AssemblyMvid : version.ContentAssemblyMvid;
                var assemblyName = main ? "Arkedo Series - 03 PIXEL!" : "Scale_Shared_ContentGame";
                var anchor = assemblies.SingleOrDefault(row => row.GetProperty("assemblyName").GetString() == assemblyName);
                if (anchor.ValueKind != JsonValueKind.Object || anchor.GetProperty("assemblySha256").GetString() != expectedHash ||
                    anchor.GetProperty("assemblyMvid").GetString() != expectedMvid)
                    throw new InvalidDataException($"Assembly guard is missing or mismatched: {assemblyName}");
                run = AssemblyRewriter.Rewrite(file.Bytes, expectedHash, expectedMvid,
                    anchor.GetProperty("methods"), fnaPath, bridgePath, main);
            }
            else if (file.RelativePath == "584E07D1/Content/Sounds/PIXEL_WaveBank.xwb")
                run = WaveBankConverter.Convert(file.Bytes, decoderDirectory, audioDecoder, privateRoot,
                    (done, total) => options.Progress?.Invoke("audio", done, total));
            else if (file.RelativePath == "584E07D1/Content/Sounds/PIXEL_SoundBank.xsb")
                run = SoundBankConverter.Convert(file.Bytes);
            outputs.Add((file.RelativePath, file.Bytes, run));
        }
        if (xnbCount != 340) throw new InvalidDataException($"Expected 340 XNB assets, found {xnbCount}.");
        var manifest = new
        {
            schemaVersion = 1,
            preparationVersion = Version,
            audioDecoderVersion = audioDecoder.GetProperty("version").GetString(),
            packageSha256 = version.PackageSha256,
            fnaSha256 = Hash(fnaBytes),
            fnaConfigSha256 = Hash(fnaConfigBytes),
            bridgeSha256 = Hash(bridgeBytes),
            hooksSha256 = Hash(hooksBytes),
            enhancements = new[] { Patches.DesktopInputPatch.Id, Patches.HelpOptionsPatch.Id, Patches.UltrawidePatch.Id },
            files = outputs.Select(row => new
            {
                path = row.Path,
                originalSha256 = Hash(row.Original),
                preparedSha256 = Hash(row.Run),
                originalBytes = row.Original.Length,
                preparedBytes = row.Run.Length
            }).ToArray()
        };
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(destination);
        try
        {
            foreach (var row in outputs)
            {
                var relative = row.Path.Replace('/', Path.DirectorySeparatorChar);
                Write(Path.Combine(destination, "original", relative), row.Original);
                Write(Path.Combine(destination, "run", relative), row.Run);
            }
            var gameDirectory = Path.GetDirectoryName(version.AssemblyPath.Replace('/', Path.DirectorySeparatorChar))!;
            Write(Path.Combine(destination, "run", gameDirectory, "FNA.dll"), fnaBytes);
            Write(Path.Combine(destination, "run", gameDirectory, "FNA.dll.config"), fnaConfigBytes);
            Write(Path.Combine(destination, "run", gameDirectory, "Port.Bridge.dll"), bridgeBytes);
            Write(Path.Combine(destination, "run", gameDirectory, "Port.Hooks.dll"), hooksBytes);
            Write(Path.Combine(destination, "manifest.json"), manifestBytes);
            foreach (var row in outputs)
            {
                var relative = row.Path.Replace('/', Path.DirectorySeparatorChar);
                if (Hash(File.ReadAllBytes(Path.Combine(destination, "original", relative))) != Hash(row.Original) ||
                    Hash(File.ReadAllBytes(Path.Combine(destination, "run", relative))) != Hash(row.Run))
                    throw new IOException($"Prepared file verification failed: {row.Path}");
            }
            foreach (var (name, expected) in new[] { ("FNA.dll", Hash(fnaBytes)),
                         ("FNA.dll.config", Hash(fnaConfigBytes)), ("Port.Bridge.dll", Hash(bridgeBytes)), ("Port.Hooks.dll", Hash(hooksBytes)) })
                if (Hash(File.ReadAllBytes(Path.Combine(destination, "run", gameDirectory, name))) != expected)
                    throw new IOException($"Prepared runtime file verification failed: {name}");
            return new PreparationResult(destination, files.Count, xnbCount, Hash(manifestBytes));
        }
        catch
        {
            Directory.Delete(destination, true);
            throw;
        }
    }

    private static SupportedVersion ReadVersion(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1) throw new InvalidDataException("Unsupported version schema.");
        var rows = root.GetProperty("packages").EnumerateArray().ToArray();
        if (rows.Length != 1) throw new InvalidDataException("Expected one pinned package version.");
        var row = rows[0];
        string S(string key) => row.GetProperty(key).GetString()!;
        int I(string key) => row.GetProperty(key).GetInt32();
        return new SupportedVersion(S("title"), S("titleId"), I("packageBytes"), S("packageSha256"),
            I("fileTableBlocks"), I("dataBlocks"), I("files"), S("assemblyPath"), S("assemblySha256"),
            S("assemblyMvid"), S("contentAssemblyPath"), S("contentAssemblySha256"), S("contentAssemblyMvid"));
    }

    private static void ValidateParents(string privateRoot, string destination)
    {
        for (var current = new DirectoryInfo(Path.GetDirectoryName(destination)!); current is not null; current = current.Parent)
        {
            if (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new IOException($"Preparation parent is a reparse point: {current.FullName}");
            if (string.Equals(current.FullName.TrimEnd(Path.DirectorySeparatorChar), privateRoot.TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase)) break;
        }
    }
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static void Write(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }
}
