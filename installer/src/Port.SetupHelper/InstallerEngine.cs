using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Port.Prepare;

namespace Port.SetupHelper;

internal static class InstallerEngine
{
    private const string PackageHash = "64b5bb62e8cf16c44c04c1b9d2ac5f1c780db1cd28e031cd4315788ab3af4104";
    private const string GameFolder = "run/584E07D1";
    private sealed record FileRow(string Path, long Bytes, string Sha256);
    private sealed record InstallManifest(int Format, string SourcePackageSha256, FileRow[] Files);

    public static void Verify(string directory)
    {
        var root = SafeTarget(directory);
        var manifest = JsonSerializer.Deserialize<InstallManifest>(File.ReadAllBytes(Path.Combine(root, "install-manifest.json")))
            ?? throw new InvalidDataException("Install manifest is empty.");
        if (manifest.Format != 1 || manifest.SourcePackageSha256 != PackageHash || manifest.Files.Length < 364)
            throw new InvalidDataException("Unsupported install manifest.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in manifest.Files)
        {
            if (!SafeRelative(row.Path) || !seen.Add(row.Path))
                throw new InvalidDataException("Invalid install manifest path.");
            var path = Path.Combine(root, row.Path.Replace('/', Path.DirectorySeparatorChar));
            RejectReparsePoints(path);
            if (!File.Exists(path) || new FileInfo(path).Length != row.Bytes || Hash(path) != row.Sha256)
                throw new InvalidDataException($"Installed file failed verification: {row.Path}");
        }
        foreach (var required in new[] { "manifest.json", $"{GameFolder}/PIXEL.exe", $"{GameFolder}/Arkedo Series - 03 PIXEL!.exe",
                     $"{GameFolder}/Scale_Shared_ContentGame.dll", $"{GameFolder}/FNA.dll", $"{GameFolder}/Port.Bridge.dll",
                     $"{GameFolder}/Port.Hooks.dll", $"{GameFolder}/SDL3.dll", $"{GameFolder}/FNA3D.dll", $"{GameFolder}/FAudio.dll" })
            if (!seen.Contains(required)) throw new InvalidDataException($"Required file missing from manifest: {required}");
        using var preparation = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "manifest.json")));
        if (preparation.RootElement.GetProperty("packageSha256").GetString() != PackageHash)
            throw new InvalidDataException("Prepared game has the wrong source package.");
        foreach (var row in preparation.RootElement.GetProperty("files").EnumerateArray())
        {
            var path = "run/" + row.GetProperty("path").GetString();
            var installed = manifest.Files.SingleOrDefault(file => file.Path == path);
            if (installed is null || installed.Sha256 != row.GetProperty("preparedSha256").GetString() ||
                installed.Bytes != row.GetProperty("preparedBytes").GetInt64())
                throw new InvalidDataException("Prepared content differs from its manifest.");
        }
    }

    public static void Install(string destination, string payload, string packagePath, string? cancelFile)
    {
        var target = SafeTarget(destination);
        var parent = Path.GetDirectoryName(target)!;
        var source = Path.GetFullPath(payload);
        var package = Path.GetFullPath(packagePath);
        if (Within(package, parent) || Within(source, parent))
            throw new ArgumentException("Keep the source package and setup payload outside the installation folder.");
        var versions = Path.Combine(source, "config", "supported-versions.json");
        var lastProgress = -1;
        void CheckCancelled()
        {
            if (cancelFile is not null && File.Exists(cancelFile)) throw new OperationCanceledException("Installation cancelled.");
        }
        void Report(string stage, int progress)
        {
            CheckCancelled();
            Console.WriteLine("STAGE " + stage);
            if (progress <= lastProgress) return;
            Console.WriteLine($"PROGRESS {progress}");
            lastProgress = progress;
        }
        Report("extract", 0);
        PreparationEngine.ValidatePackage(package, versions);
        CheckCancelled();
        Directory.CreateDirectory(parent);
        var backup = target + ".previous";
        if (Directory.Exists(backup))
        {
            Verify(backup);
            if (Directory.Exists(target)) Directory.Delete(target, true);
            Directory.Move(backup, target);
        }
        var work = Path.Combine(parent, ".port-work-" + Guid.NewGuid().ToString("N"));
        var staged = Path.Combine(work, "current");
        try
        {
            Directory.CreateDirectory(work);
            var runtime = Path.Combine(source, "runtime");
            bool reuse = false;
            Report("reuse-check", 40);
            if (Directory.Exists(target))
            {
                try
                {
                    Verify(target);
                    using var old = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(target, "manifest.json")));
                    var manifest = old.RootElement;
                    reuse = manifest.GetProperty("preparationVersion").GetString() == PreparationEngine.Version &&
                        manifest.GetProperty("fnaSha256").GetString() == Hash(Path.Combine(runtime, "FNA.dll")) &&
                        manifest.GetProperty("fnaConfigSha256").GetString() == Hash(Path.Combine(runtime, "FNA.dll.config")) &&
                        manifest.GetProperty("bridgeSha256").GetString() == Hash(Path.Combine(runtime, "Port.Bridge.dll")) &&
                        manifest.GetProperty("hooksSha256").GetString() == Hash(Path.Combine(runtime, "Port.Hooks.dll"));
                }
                catch (Exception error) when (error is IOException or InvalidDataException or JsonException or KeyNotFoundException)
                {
                    Console.WriteLine("REUSE_UNAVAILABLE " + error.Message);
                }
            }
            if (reuse)
            {
                Report("reuse", 100);
                // Only reuse the outputs recorded by the preparation engine.
                using var old = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(target, "manifest.json")));
                foreach (var row in old.RootElement.GetProperty("files").EnumerateArray())
                {
                    CheckCancelled();
                    var relative = "run/" + row.GetProperty("path").GetString();
                    if (!SafeRelative(relative)) throw new InvalidDataException("Invalid prepared path.");
                    CopyFile(Path.Combine(target, relative), Path.Combine(staged, relative));
                }
                CopyFile(Path.Combine(target, "manifest.json"), Path.Combine(staged, "manifest.json"));
            }
            else
            {
                var prepared = Path.Combine(work, "prepared");
                PreparationEngine.Prepare(new PreparationOptions(package, prepared, work, versions,
                    Path.Combine(source, "config", "patch-anchors.json"), Path.Combine(runtime, "FNA.dll"),
                    Path.Combine(runtime, "Port.Bridge.dll"), Path.Combine(source, "tools", "audio"),
                    (stage, done, total) => Report(stage, stage == "audio"
                        ? 400 + 300 * done / Math.Max(1, total) : 100 + 600 * done / Math.Max(1, total))));
                Directory.CreateDirectory(staged);
                Directory.Move(Path.Combine(prepared, "run"), Path.Combine(staged, "run"));
                CopyFile(Path.Combine(prepared, "manifest.json"), Path.Combine(staged, "manifest.json"));
            }
            Report("runtime", 750);
            foreach (var file in Directory.EnumerateFiles(runtime, "*", SearchOption.AllDirectories))
            {
                CheckCancelled();
                RejectReparsePoints(file);
                CopyFile(file, Path.Combine(staged, GameFolder, Path.GetRelativePath(runtime, file)));
            }
            Report("verify", 850);
            var files = Directory.EnumerateFiles(staged, "*", SearchOption.AllDirectories)
                .Select(file => new FileRow(Path.GetRelativePath(staged, file).Replace('\\', '/'), new FileInfo(file).Length, Hash(file)))
                .OrderBy(row => row.Path, StringComparer.Ordinal).ToArray();
            File.WriteAllText(Path.Combine(staged, "install-manifest.json"),
                JsonSerializer.Serialize(new InstallManifest(1, PackageHash, files), new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(Path.Combine(staged, "install-report.txt"),
                $"Source package SHA-256: {PackageHash}\nPreparation version: {PreparationEngine.Version}\nVerified files: {files.Length}\nReused content: {reuse}\n");
            Verify(staged);
            CheckLoad(Path.Combine(staged, GameFolder));
            Report("commit", 990);
            if (Directory.Exists(target)) Directory.Move(target, backup);
            try { Directory.Move(staged, target); }
            catch
            {
                if (Directory.Exists(backup)) Directory.Move(backup, target);
                throw;
            }
            // Inno calls finalize only after its own registration succeeds.
            Console.WriteLine("PROGRESS 1000");
        }
        finally
        {
            if (Directory.Exists(work)) Directory.Delete(work, true);
        }
    }

    public static void Rollback(string destination)
    {
        var target = SafeTarget(destination);
        var backup = target + ".previous";
        if (Directory.Exists(backup))
        {
            Verify(backup);
            if (Directory.Exists(target)) Directory.Delete(target, true);
            Directory.Move(backup, target);
        }
        else if (Directory.Exists(target))
        {
            Verify(target);
            Directory.Delete(target, true);
        }
    }

    public static void FinalizeInstall(string destination)
    {
        var target = SafeTarget(destination);
        Verify(target);
        var backup = target + ".previous";
        RejectReparsePoints(backup);
        if (Directory.Exists(backup)) Directory.Delete(backup, true);
    }

    private static void CheckLoad(string directory)
    {
        var info = new ProcessStartInfo(Path.Combine(directory, "PIXEL.exe"))
        {
            WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        info.ArgumentList.Add("--check-load");
        using var process = Process.Start(info) ?? throw new IOException("Could not verify the prepared launcher.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new IOException("Prepared game load check timed out.");
        }
        Task.WaitAll(stdout, stderr);
        Console.Write(stdout.Result);
        if (process.ExitCode != 0) throw new InvalidDataException("Prepared game load check failed: " + stderr.Result);
    }

    private static string SafeTarget(string path)
    {
        var target = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        if (Path.GetFileName(target) is not ("current" or "current.previous"))
            throw new ArgumentException("The installation destination must be named current.");
        RejectReparsePoints(target);
        return target;
    }
    private static void RejectReparsePoints(string path)
    {
        for (var entry = new FileInfo(Path.GetFullPath(path)) as FileSystemInfo; entry is not null;
             entry = new DirectoryInfo(Path.GetDirectoryName(entry.FullName)!))
        {
            if (Path.Exists(entry.FullName) && File.GetAttributes(entry.FullName).HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("Installation paths cannot contain reparse points.");
            if (Path.GetDirectoryName(entry.FullName) is null) break;
        }
    }
    private static bool Within(string path, string root) => path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static bool SafeRelative(string path) => path.Length != 0 && !Path.IsPathRooted(path) &&
        path.Split('/').All(part => part.Length != 0 && part is not ("." or "..") && part.IndexOfAny(['\\', ':']) < 0);
    private static void CopyFile(string source, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, overwrite: true);
    }
    private static string Hash(string path)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(file));
    }
}
