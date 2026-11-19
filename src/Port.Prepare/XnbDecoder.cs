using System.Buffers.Binary;
using System.Reflection;

namespace Port.Prepare;

internal sealed class XnbDecoder
{
    private readonly ConstructorInfo constructor;
    private readonly MethodInfo decompress;

    public XnbDecoder(string fnaPath)
    {
        var decoder = Assembly.LoadFrom(Path.GetFullPath(fnaPath))
            .GetType("Microsoft.Xna.Framework.Content.LzxDecoder", true)!;
        constructor = decoder.GetConstructor([typeof(int)])
            ?? throw new MissingMethodException("Pinned FNA LzxDecoder(int) is missing.");
        decompress = decoder.GetMethod("Decompress", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMethodException("Pinned FNA LzxDecoder.Decompress is missing.");
    }

    public byte[] Decode(byte[] bytes, string relative)
    {
        if (bytes.Length < 10 || bytes[0] != 'X' || bytes[1] != 'N' || bytes[2] != 'B' ||
            bytes[3] != 'x' || bytes[4] != 4 ||
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(6, 4)) != bytes.Length)
            throw new InvalidDataException($"Invalid or unsupported XNB header: {relative}");
        byte[] payload;
        if ((bytes[5] & 0x80) == 0)
            payload = bytes[10..];
        else
        {
            if (bytes.Length < 14) throw new InvalidDataException($"Truncated XNB: {relative}");
            int uncompressedSize = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(10, 4));
            if (uncompressedSize is < 1 or > 256_000_000)
                throw new InvalidDataException($"Invalid XNB expansion size: {relative}");
            using var compressed = new MemoryStream(bytes, 14, bytes.Length - 14, false);
            using var expanded = new MemoryStream(new byte[uncompressedSize], true);
            var instance = constructor.Invoke([16]);
            while (compressed.Position < compressed.Length)
            {
                int high = compressed.ReadByte(), low = compressed.ReadByte();
                if (high < 0 || low < 0) throw new EndOfStreamException(relative);
                int blockSize = high << 8 | low;
                int frameSize = 0x8000;
                if (high == 0xFF)
                {
                    int frameLow = compressed.ReadByte(), blockHigh = compressed.ReadByte(), blockLow = compressed.ReadByte();
                    if (frameLow < 0 || blockHigh < 0 || blockLow < 0) throw new EndOfStreamException(relative);
                    frameSize = low << 8 | frameLow;
                    blockSize = blockHigh << 8 | blockLow;
                }
                if (blockSize == 0 || frameSize == 0) break;
                if (compressed.Position + blockSize > compressed.Length)
                    throw new InvalidDataException($"Invalid XNB LZX frame: {relative}");
                long blockEnd = compressed.Position + blockSize;
                int code = (int)decompress.Invoke(instance, [compressed, blockSize, expanded, frameSize])!;
                if (code != 0)
                    throw new InvalidDataException($"XNB LZX decoder rejected {relative}: {code}");
                compressed.Position = blockEnd;
            }
            if (expanded.Position != uncompressedSize)
                throw new InvalidDataException($"Decompressed XNB length mismatch: {relative}");
            payload = expanded.ToArray();
        }
        var output = new byte[10 + payload.Length];
        Array.Copy(bytes, output, 6);
        output[5] &= 0x7F;
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(6, 4), output.Length);
        payload.CopyTo(output, 10);
        return output;
    }
}
