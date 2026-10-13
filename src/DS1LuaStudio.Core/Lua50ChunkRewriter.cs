using System.Text;

namespace DS1LuaStudio.Core;

/// <summary>
/// Rewrites the <c>size_t</c> width of a Lua 5.0 chunk.
/// <para>
/// Dark Souls Prepare to Die Edition's Lua was built with a 32-bit <c>size_t</c>, so its string
/// lengths are 32-bit. The bundled Lua 5.0 compiler (<c>lua502.dll</c>) was built with a 64-bit
/// <c>size_t</c> and emits 64-bit lengths, which the game cannot load.
/// </para>
/// <para>
/// A Lua 5.0 chunk is a pure byte stream - it contains no absolute file offsets, only counts - so
/// changing the width of the length prefixes is a structural transform. Each function is parsed
/// and re-emitted with the target width, recursing into nested functions.
/// </para>
/// </summary>
public static class Lua50ChunkRewriter
{
    /// <summary>Bytes occupied by a Lua 5.0 chunk header (signature, sizes, test number).</summary>
    private const int HeaderSize = 22;

    /// <summary>
    /// Converts a Lua 5.0 chunk to the requested <c>size_t</c> width, detecting the source width
    /// from the header.
    /// </summary>
    public static byte[] ConvertSizeT(byte[] chunk, int targetSizeT)
    {
        if (targetSizeT is not (4 or 8))
            throw new ArgumentOutOfRangeException(nameof(targetSizeT), targetSizeT, "size_t must be 4 or 8 bytes.");

        int sourceSizeT = LuaBytecodeService.ReadSizeTSize(chunk);
        if (sourceSizeT == 0)
            throw new ArgumentException("Not a Lua 5.0 chunk with a recognisable size_t width.", nameof(chunk));

        return ConvertSizeT(chunk, sourceSizeT, targetSizeT);
    }

    public static byte[] ConvertSizeT(byte[] chunk, int sourceSizeT, int targetSizeT)
    {
        // No short-circuit when the widths already match: re-emitting proves the parser and
        // serialiser are exact inverses, which is what the round-trip tests rely on.
        var reader = new Reader(chunk, HeaderSize);
        var writer = new Writer();

        // Copy the header verbatim, then correct the size_t field (byte 7).
        writer.WriteBytes(chunk.AsSpan(0, HeaderSize));
        writer.Patch(7, (byte)targetSizeT);

        ReadFunction(reader, writer, sourceSizeT, targetSizeT);

        return writer.ToArray();
    }

    private static void ReadFunction(Reader reader, Writer writer, int sourceSizeT, int targetSizeT)
    {
        // string source
        WriteString(reader, writer, sourceSizeT, targetSizeT);
        writer.WriteInt32(reader.ReadInt32());       // linedefined
        writer.WriteByte(reader.ReadByte());         // nups
        writer.WriteByte(reader.ReadByte());         // numparams
        writer.WriteByte(reader.ReadByte());         // is_vararg
        writer.WriteByte(reader.ReadByte());         // maxstacksize

        int lineInfoCount = reader.ReadInt32();
        writer.WriteInt32(lineInfoCount);
        for (int i = 0; i < lineInfoCount; i++) writer.WriteInt32(reader.ReadInt32());

        // Locvar { string name; int startpc; int endpc; }
        int localCount = reader.ReadInt32();
        writer.WriteInt32(localCount);
        for (int i = 0; i < localCount; i++)
        {
            WriteString(reader, writer, sourceSizeT, targetSizeT);
            writer.WriteInt32(reader.ReadInt32());   // startpc
            writer.WriteInt32(reader.ReadInt32());   // endpc
        }

        // Upvalue names
        int upValueCount = reader.ReadInt32();
        writer.WriteInt32(upValueCount);
        for (int i = 0; i < upValueCount; i++)
            WriteString(reader, writer, sourceSizeT, targetSizeT);

        // Constants
        int constantCount = reader.ReadInt32();
        writer.WriteInt32(constantCount);
        for (int i = 0; i < constantCount; i++)
            ReadConstant(reader, writer, sourceSizeT, targetSizeT);

        // Nested functions come before this function's bytecode in the DS1/from layout.
        int childCount = reader.ReadInt32();
        writer.WriteInt32(childCount);
        for (int i = 0; i < childCount; i++)
            ReadFunction(reader, writer, sourceSizeT, targetSizeT);

        // Bytecode: a count of 32-bit instructions, opaque to us.
        int instructionCount = reader.ReadInt32();
        writer.WriteInt32(instructionCount);
        writer.WriteBytes(reader.ReadBytes(instructionCount * 4));
    }

    private static void ReadConstant(Reader reader, Writer writer, int sourceSizeT, int targetSizeT)
    {
        byte type = reader.ReadByte();
        writer.WriteByte(type);

        switch (type)
        {
            case 1:      // boolean
                writer.WriteByte(reader.ReadByte());
                break;
            case 3:      // number, stored as a double in both DS1 and lua502 chunks
                writer.WriteBytes(reader.ReadBytes(8));
                break;
            case 4:      // string
            case 0x14:   // string (alternate tag)
                WriteString(reader, writer, sourceSizeT, targetSizeT);
                break;
            case 0x13:   // integer literal
                writer.WriteBytes(reader.ReadBytes(sourceSizeT == 4 ? 4 : 8));
                break;
            default:     // nil (and anything unknown): just the tag byte
                break;
        }
    }

    /// <summary>
    /// Copies a length-prefixed string, re-encoding the length prefix. The stored length includes
    /// the trailing NUL, which is preserved.
    /// </summary>
    private static void WriteString(Reader reader, Writer writer, int sourceSizeT, int targetSizeT)
    {
        ulong length = reader.ReadSizeT(sourceSizeT);
        if (length == 0)
        {
            // A zero length means "no string"; emit the target width zero.
            WriteSizeT(writer, 0, targetSizeT);
            return;
        }

        if (length > int.MaxValue)
            throw new InvalidDataException($"Implausible Lua string length: {length}");

        byte[] payload = reader.ReadBytes((int)length);   // text + trailing NUL
        WriteSizeT(writer, (ulong)payload.Length, targetSizeT);
        writer.WriteBytes(payload);
    }

    private static void WriteSizeT(Writer writer, ulong value, int targetSizeT)
    {
        if (targetSizeT == 4) writer.WriteUInt32((uint)value);
        else writer.WriteUInt64(value);
    }

    private sealed class Reader(byte[] data, int position)
    {
        private readonly byte[] _data = data;
        private int _position = position;

        public byte ReadByte()
        {
            if (_position >= _data.Length) throw new EndOfStreamException("Unexpected end of Lua chunk.");
            return _data[_position++];
        }

        public int ReadInt32()
        {
            int value = _data[_position]
                        | (_data[_position + 1] << 8)
                        | (_data[_position + 2] << 16)
                        | (_data[_position + 3] << 24);
            _position += 4;
            return value;
        }

        public ulong ReadSizeT(int size)
        {
            if (size == 4)
            {
                ulong v = (uint)(_data[_position]
                                 | (_data[_position + 1] << 8)
                                 | (_data[_position + 2] << 16)
                                 | (_data[_position + 3] << 24));
                _position += 4;
                return v;
            }

            ulong value = 0;
            for (int i = 0; i < 8; i++) value |= (ulong)_data[_position + i] << (8 * i);
            _position += 8;
            return value;
        }

        public byte[] ReadBytes(int count)
        {
            if (count < 0 || _position + count > _data.Length)
                throw new EndOfStreamException($"Unexpected end of Lua chunk reading {count} bytes.");
            var slice = new byte[count];
            Array.Copy(_data, _position, slice, 0, count);
            _position += count;
            return slice;
        }
    }

    private sealed class Writer
    {
        private readonly List<byte> _bytes = new(4096);

        public void WriteByte(byte value) => _bytes.Add(value);

        public void WriteInt32(int value)
        {
            _bytes.Add((byte)value);
            _bytes.Add((byte)(value >> 8));
            _bytes.Add((byte)(value >> 16));
            _bytes.Add((byte)(value >> 24));
        }

        public void WriteUInt32(uint value) => WriteInt32(unchecked((int)value));

        public void WriteUInt64(ulong value)
        {
            for (int i = 0; i < 8; i++) _bytes.Add((byte)(value >> (8 * i)));
        }

        public void WriteBytes(ReadOnlySpan<byte> value)
        {
            foreach (byte b in value) _bytes.Add(b);
        }

        public void Patch(int index, byte value) => _bytes[index] = value;

        public byte[] ToArray() => _bytes.ToArray();
    }
}
