using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace Port.Prepare;

internal sealed record PackageFile(string RelativePath, byte[] Bytes, string Sha256);

internal sealed class StfsPackage
{
    private const int BlockSize = 0x1000;
    private readonly byte[] data;
    private readonly int headerSize;
    private readonly int totalBlocks;
    private readonly int tableBlocks;

    private StfsPackage(byte[] data, int headerSize, int totalBlocks, int tableBlocks)
    {
        this.data = data;
        this.headerSize = headerSize;
        this.totalBlocks = totalBlocks;
        this.tableBlocks = tableBlocks;
    }

    public static IReadOnlyList<PackageFile> Read(string path, SupportedVersion version)
    {
        var bytes = File.ReadAllBytes(path);
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (bytes.Length != version.PackageBytes || digest != version.PackageSha256)
            throw new InvalidDataException($"Unsupported or corrupt LIVE package: SHA-256 {digest}.");
        if (bytes.Length < 0xC000 || !bytes.AsSpan(0, 4).SequenceEqual("LIVE"u8))
            throw new InvalidDataException("Expected a LIVE/STFS package.");
        int header = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(0x340, 4)));
        var descriptor = bytes.AsSpan(0x379, 0x24);
        int tableCount = BinaryPrimitives.ReadUInt16LittleEndian(descriptor.Slice(3, 2));
        int tableStart = Read24(descriptor.Slice(5, 3));
        int blockCount = checked((int)BinaryPrimitives.ReadUInt32BigEndian(descriptor.Slice(0x1C, 4)));
        if (descriptor[0] != 0x24 || (descriptor[2] & 1) == 0 || tableCount is < 1 or > 16 || tableStart != 0 ||
            blockCount is < 1 or >= 28900 || tableCount != version.FileTableBlocks || blockCount != version.DataBlocks)
            throw new InvalidDataException("Unsupported STFS volume layout.");
        var package = new StfsPackage(bytes, header, blockCount, tableCount);
        if (package.DataOffset(blockCount - 1) + BlockSize > bytes.Length)
            throw new InvalidDataException("STFS block count exceeds package size.");
        return package.ReadFiles(version);
    }

    private IReadOnlyList<PackageFile> ReadFiles(SupportedVersion version)
    {
        var table = ReadBlocks(0, tableBlocks, tableBlocks * BlockSize);
        var entries = new List<Entry>();
        var terminated = false;
        for (var index = 0; index < tableBlocks * 64; index++)
        {
            var row = table.AsSpan(index * 64, 64);
            if (row[0] == 0) { terminated = true; break; }
            int flags = row[40];
            if ((flags & 0x40) == 0) throw new InvalidDataException("Noncontiguous STFS entry is unsupported.");
            int nameLength = flags & 0x3F;
            var name = Encoding.ASCII.GetString(row.Slice(0, nameLength));
            ValidateName(name);
            entries.Add(new Entry(name, (flags & 0x80) != 0, Read24(row.Slice(44, 3)),
                Read24(row.Slice(47, 3)), BinaryPrimitives.ReadUInt16BigEndian(row.Slice(50, 2)),
                checked((int)BinaryPrimitives.ReadUInt32BigEndian(row.Slice(52, 4)))));
        }
        if (!terminated) throw new InvalidDataException("STFS file table has no terminator.");
        var used = new HashSet<int>(Enumerable.Range(0, tableBlocks));
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<PackageFile>();
        foreach (var (entry, index) in entries.Select((entry, index) => (entry, index)))
        {
            var parts = new List<string> { entry.Name };
            var visited = new HashSet<int> { index };
            int parent = entry.Parent;
            while (parent != 0xFFFF)
            {
                if (parent >= entries.Count || !visited.Add(parent) || !entries[parent].Directory)
                    throw new InvalidDataException("Invalid STFS directory parent.");
                parts.Insert(0, entries[parent].Name);
                parent = entries[parent].Parent;
            }
            var relative = string.Join('/', parts);
            if (!paths.Add(relative)) throw new InvalidDataException($"Duplicate STFS path: {relative}");
            if (entry.Directory) continue;
            if (entry.Blocks != (entry.Length + BlockSize - 1) / BlockSize || entry.Start < tableBlocks ||
                entry.Start + entry.Blocks > totalBlocks)
                throw new InvalidDataException($"Invalid STFS block range: {relative}");
            for (int block = entry.Start; block < entry.Start + entry.Blocks; block++)
                if (!used.Add(block)) throw new InvalidDataException($"Overlapping STFS block: {block}");
            var payload = ReadBlocks(entry.Start, entry.Blocks, entry.Length);
            files.Add(new PackageFile(relative, payload, Convert.ToHexStringLower(SHA256.HashData(payload))));
        }
        if (used.Count != totalBlocks || files.Count != version.Files)
            throw new InvalidDataException("STFS file table does not account for the expected files and blocks.");
        var info = files.SingleOrDefault(file => file.RelativePath == "GameInfo.xml")
            ?? throw new InvalidDataException("GameInfo.xml is missing.");
        using var infoStream = new MemoryStream(info.Bytes, false);
        var title = XDocument.Load(infoStream).Descendants("TitleInfo").Single();
        if ((string?)title.Attribute("Name") != version.Title ||
            !uint.TryParse((string?)title.Attribute("TitleID"), out var titleId) ||
            titleId != Convert.ToUInt32(version.TitleId[2..], 16))
            throw new InvalidDataException("Unexpected game title metadata.");
        foreach (var (name, hash) in new[] { (version.AssemblyPath, version.AssemblySha256),
                     (version.ContentAssemblyPath, version.ContentAssemblySha256) })
            if (files.SingleOrDefault(file => file.RelativePath == name)?.Sha256 != hash)
                throw new InvalidDataException($"Unexpected game assembly: {name}");
        return files;
    }

    private byte[] ReadBlocks(int start, int count, int length)
    {
        var result = new byte[checked(count * BlockSize)];
        for (var index = 0; index < count; index++)
        {
            int block = start + index;
            int offset = DataOffset(block);
            int hashOffset = HashOffset(block);
            if (offset < 0 || offset + BlockSize > data.Length || hashOffset < 0 || hashOffset + 20 > data.Length)
                throw new InvalidDataException($"STFS block {block} lies outside the package.");
            var span = data.AsSpan(offset, BlockSize);
            if (!SHA1.HashData(span).AsSpan().SequenceEqual(data.AsSpan(hashOffset, 20)))
                throw new InvalidDataException($"STFS data block {block} fails its SHA-1 entry.");
            span.CopyTo(result.AsSpan(index * BlockSize, BlockSize));
        }
        if (length > result.Length) throw new InvalidDataException("File exceeds allocated STFS blocks.");
        return result.AsSpan(0, length).ToArray();
    }

    private int DataOffset(int block)
    {
        int extra = (block + 170) / 170 + (block >= 170 ? 1 : 0);
        return checked(((headerSize + BlockSize - 1) / BlockSize + block + extra) * BlockSize);
    }
    private int HashOffset(int block)
    {
        int tableBlock = block < 170 ? 0 : (block / 170) * 171 + 1;
        return checked(((headerSize + BlockSize - 1) / BlockSize + tableBlock) * BlockSize + block % 170 * 0x18);
    }
    private static int Read24(ReadOnlySpan<byte> bytes) => bytes[0] | bytes[1] << 8 | bytes[2] << 16;
    private static void ValidateName(string name)
    {
        if (name.Length == 0 || name is "." or ".." || name.EndsWith(' ') || name.EndsWith('.') ||
            name.Any(c => c < 32 || c > 126 || "<>:\"/\\|?*".Contains(c)) ||
            new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                    "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }
                .Contains(name.Split('.')[0], StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"Unsafe STFS name: {name}");
    }
    private sealed record Entry(string Name, bool Directory, int Blocks, int Start, int Parent, int Length);
}
