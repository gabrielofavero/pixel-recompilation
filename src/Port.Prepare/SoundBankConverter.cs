using System.Buffers.Binary;

namespace Port.Prepare;

internal static class SoundBankConverter
{
    // FAudio prepares interactive cues without a sound, then starts them only on
    // a variable change. PIXEL never sets cue variables or applies 3D audio. Its
    // five interactive tables each select one sound at the default value zero.
    // Make those cues select that sound directly. Preserve all other cue fields,
    // sound/event data, transitions, and the four random variation tables.
    internal static byte[] Convert(byte[] original)
    {
        uint U(int at) => BinaryPrimitives.ReadUInt32BigEndian(original.AsSpan(at, 4));
        ushort H(int at) => BinaryPrimitives.ReadUInt16BigEndian(original.AsSpan(at, 2));
        if (original.Length != 4352 || !original.AsSpan(0, 4).SequenceEqual("KBDS"u8) ||
            H(4) != 45 || H(6) != 43 || H(19) != 54 || H(21) != 9 || U(50) != 2100 || U(54) != 2306)
            throw new InvalidDataException("Unsupported Xbox sound bank layout.");
        using var output = new MemoryStream();
        output.Write(original);
        var direct = new Dictionary<uint, uint>();
        var relocated = new Dictionary<uint, uint>();
        int position = 2100;
        for (int index = 0; index < 9; index++)
        {
            uint header = U(position);
            int count = (int)(header & 0xFFFF), type = (int)(header >> 19 & 7);
            if (type == 3 && count == 1)
            {
                int variable = H(position + 6);
                float min = BitConverter.Int32BitsToSingle((int)U(position + 12));
                float max = BitConverter.Int32BitsToSingle((int)U(position + 16));
                if (variable is not (6 or 7) || min != 0 || max != (variable == 6 ? 180 : 0))
                    throw new InvalidDataException("Interactive cue selection differs from the supported default.");
                direct.Add((uint)position, U(position + 8));
                position += 24;
            }
            else if (type == 1 && count is 2 or 3)
            {
                int length = 8 + count * 6;
                relocated.Add((uint)position, checked((uint)output.Position));
                output.Write(original.AsSpan(position, length));
                position += length;
            }
            else throw new InvalidDataException("Unexpected sound bank variation table.");
        }
        if (position != 2306 || direct.Count != 5 || relocated.Count != 4)
            throw new InvalidDataException("Sound bank variation counts differ.");
        var result = output.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(50, 4), (uint)original.Length);
        var referenced = new HashSet<uint>();
        for (int index = 0; index < 9; index++)
        {
            int at = checked((int)U(38)) + index * 15;
            uint code = U(at + 1);
            if (!referenced.Add(code) || (original[at] & 4) != 0)
                throw new InvalidDataException("Complex cue mapping differs.");
            if (direct.TryGetValue(code, out uint sound))
            {
                result[at] |= 4;
                BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(at + 1, 4), sound);
            }
            else if (relocated.TryGetValue(code, out uint table))
                BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(at + 1, 4), table);
            else throw new InvalidDataException("Cue has an unknown variation table.");
        }
        return result;
    }
}
