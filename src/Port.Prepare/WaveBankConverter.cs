using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace Port.Prepare;

// The supported bank is XACT 3, big endian, with 52 PCM16 and 19 XMA2 entries.
// Keep entry order, sample counts and loop regions; only replace the sample encoding.
internal static class WaveBankConverter
{
    internal static byte[] Convert(byte[] source, string decoderDirectory, JsonElement decoder, string temporaryRoot)
    {
        foreach (var file in decoder.GetProperty("files").EnumerateArray())
        {
            var path = Path.Combine(decoderDirectory, file.GetProperty("name").GetString()!);
            if (!File.Exists(path) || System.Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))) !=
                file.GetProperty("sha256").GetString())
                throw new InvalidDataException($"Audio decoder dependency hash differs: {Path.GetFileName(path)}");
        }
        uint U(int offset) => BinaryPrimitives.ReadUInt32BigEndian(source.AsSpan(offset, 4));
        if (source.Length < 148 || !source.AsSpan(0, 4).SequenceEqual("DNBW"u8) || U(4) != 45 || U(8) != 43 ||
            U(12) != 52 || U(16) != 96 || U(52) != 0x80000 || U(56) != 71 || U(124) != 24 || U(132) != 2048)
            throw new InvalidDataException("Unsupported Xbox wave bank layout.");
        int metadata = checked((int)U(20)), waveData = checked((int)U(44));
        var temporary = Path.Combine(temporaryRoot, ".audio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var input = Path.Combine(temporary, "source.xwb");
            File.WriteAllBytes(input, source);
            using var payload = new MemoryStream();
            var entries = new List<uint[]>();
            int xma = 0, pcm = 0;
            for (int index = 0; index < 71; index++)
            {
                int at = checked(metadata + index * 24);
                uint flags = U(at), format = U(at + 4), start = U(at + 8), length = U(at + 12);
                int channels = (int)(format >> 2 & 7), rate = (int)(format >> 5 & 0x3FFFF);
                int samples = (int)(flags >> 4);
                if (channels is < 1 or > 2 || format >> 31 != 1 || rate is < 8000 or > 48000)
                    throw new InvalidDataException($"Unsupported wave format at entry {index}.");
                byte[] data;
                if ((format & 3) == 0)
                {
                    data = source.AsSpan(checked(waveData + (int)start), checked((int)length)).ToArray();
                    for (int offset = 0; offset < data.Length; offset += 2)
                        (data[offset], data[offset + 1]) = (data[offset + 1], data[offset]);
                    pcm++;
                }
                else if ((format & 3) == 1)
                {
                    var output = Path.Combine(temporary, "decoded.wav");
                    Decode(Path.Combine(decoderDirectory, "vgmstream-cli.exe"), input, output, index + 1);
                    data = ReadPcm(File.ReadAllBytes(output), channels, rate);
                    File.Delete(output);
                    xma++;
                }
                else throw new InvalidDataException($"Unsupported wave codec at entry {index}.");
                if (data.Length != checked(samples * channels * 2))
                    throw new InvalidDataException($"Decoded sample count differs at wave {index}: {data.Length} bytes, expected {samples * channels * 2}.");
                uint loopStart = U(at + 16), loopLength = U(at + 20);
                if ((ulong)loopStart + loopLength > (uint)samples)
                    throw new InvalidDataException($"Invalid loop region at wave {index}.");
                entries.Add([flags, (uint)(channels << 2 | rate << 5 | channels * 2 << 23) | 0x80000000,
                    checked((uint)payload.Length), checked((uint)data.Length), loopStart, loopLength]);
                payload.Write(data);
                while (payload.Length % 4 != 0) payload.WriteByte(0);
            }
            if (xma != 19 || pcm != 52) throw new InvalidDataException("Wave bank codec counts differ.");
            using var result = new MemoryStream();
            using var writer = new BinaryWriter(result);
            const int dataOffset = 52 + 96 + 71 * 24;
            writer.Write("WBND"u8);
            writer.Write(45); writer.Write(43);
            foreach (int value in new[] { 52, 96, 148, 71 * 24, 0, 0, 0, 0, dataOffset, checked((int)payload.Length) })
                writer.Write(value);
            writer.Write(0); writer.Write(71);
            writer.Write(source.AsSpan(60, 64)); // Original bank name used by the sound bank.
            writer.Write(24); writer.Write(0); writer.Write(4); writer.Write(0); writer.Write(0L);
            foreach (var entry in entries) foreach (uint value in entry) writer.Write(value);
            writer.Write(payload.ToArray());
            return result.ToArray();
        }
        finally { Directory.Delete(temporary, true); }
    }

    private static void Decode(string executable, string input, string output, int subsong)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!
        };
        foreach (var argument in new[] { "-i", "-s", subsong.ToString(System.Globalization.CultureInfo.InvariantCulture), "-o", output, input })
            info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new IOException("Could not start the audio decoder.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new IOException($"Audio decoding timed out at entry {subsong - 1}.");
        }
        Task.WaitAll(stdout, stderr);
        if (process.ExitCode != 0 || !File.Exists(output))
            throw new InvalidDataException($"Audio decoding failed at entry {subsong - 1}: {stderr.Result}");
    }

    private static byte[] ReadPcm(byte[] wav, int channels, int rate)
    {
        if (wav.Length < 12 || !wav.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !wav.AsSpan(8, 4).SequenceEqual("WAVE"u8))
            throw new InvalidDataException("Decoder output is not RIFF/WAVE.");
        bool validFormat = false;
        byte[]? pcm = null;
        for (int at = 12; at <= wav.Length - 8;)
        {
            int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(at + 4, 4)));
            var chunk = wav.AsSpan(at + 8, length);
            if (wav.AsSpan(at, 4).SequenceEqual("fmt "u8))
                validFormat = length >= 16 && BinaryPrimitives.ReadUInt16LittleEndian(chunk) == 1 &&
                    BinaryPrimitives.ReadUInt16LittleEndian(chunk[2..]) == channels &&
                    BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]) == rate &&
                    BinaryPrimitives.ReadUInt16LittleEndian(chunk[14..]) == 16;
            if (wav.AsSpan(at, 4).SequenceEqual("data"u8)) pcm = chunk.ToArray();
            at = checked(at + 8 + length + (length & 1));
        }
        return validFormat && pcm is not null ? pcm : throw new InvalidDataException("Decoder output is not the expected PCM16 format.");
    }
}
