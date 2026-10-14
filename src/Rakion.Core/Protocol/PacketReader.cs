using System.Buffers.Binary;
using System.Text;

namespace Rakion.Core.Protocol;

/// <summary>
/// High-performance ref struct reader for parsing Rakion binary packets in Little-Endian format.
/// Emulates the extraction semantics of CNetMessage / PacketReader.
/// </summary>
public ref struct PacketReader
{
    private readonly ReadOnlySpan<byte> _buffer;
    private int _position;

    public PacketReader(ReadOnlySpan<byte> buffer)
    {
        _buffer = buffer;
        _position = 0;
    }

    public readonly int Position => _position;
    public readonly int Length => _buffer.Length;
    public readonly int Remaining => _buffer.Length - _position;
    public readonly bool HasData => _position < _buffer.Length;

    public void Seek(int position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(position, _buffer.Length);
        _position = position;
    }

    public void Skip(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        EnsureAvailable(count);
        _position += count;
    }

    public byte ReadByte()
    {
        EnsureAvailable(sizeof(byte));
        return _buffer[_position++];
    }

    public bool ReadBoolean() => ReadByte() != 0;

    public short ReadInt16()
    {
        EnsureAvailable(sizeof(short));
        short value = BinaryPrimitives.ReadInt16LittleEndian(_buffer.Slice(_position, sizeof(short)));
        _position += sizeof(short);
        return value;
    }

    public ushort ReadUInt16()
    {
        EnsureAvailable(sizeof(ushort));
        ushort value = BinaryPrimitives.ReadUInt16LittleEndian(_buffer.Slice(_position, sizeof(ushort)));
        _position += sizeof(ushort);
        return value;
    }

    public int ReadInt32()
    {
        EnsureAvailable(sizeof(int));
        int value = BinaryPrimitives.ReadInt32LittleEndian(_buffer.Slice(_position, sizeof(int)));
        _position += sizeof(int);
        return value;
    }

    public uint ReadUInt32()
    {
        EnsureAvailable(sizeof(uint));
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(_buffer.Slice(_position, sizeof(uint)));
        _position += sizeof(uint);
        return value;
    }

    public long ReadInt64()
    {
        EnsureAvailable(sizeof(long));
        long value = BinaryPrimitives.ReadInt64LittleEndian(_buffer.Slice(_position, sizeof(long)));
        _position += sizeof(long);
        return value;
    }

    public ulong ReadUInt64()
    {
        EnsureAvailable(sizeof(ulong));
        ulong value = BinaryPrimitives.ReadUInt64LittleEndian(_buffer.Slice(_position, sizeof(ulong)));
        _position += sizeof(ulong);
        return value;
    }

    public float ReadSingle()
    {
        EnsureAvailable(sizeof(float));
        float value = BinaryPrimitives.ReadSingleLittleEndian(_buffer.Slice(_position, sizeof(float)));
        _position += sizeof(float);
        return value;
    }

    public ReadOnlySpan<byte> ReadBytes(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        EnsureAvailable(count);
        ReadOnlySpan<byte> slice = _buffer.Slice(_position, count);
        _position += count;
        return slice;
    }

    public string ReadFixedString(int length, Encoding? encoding = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        EnsureAvailable(length);

        ReadOnlySpan<byte> slice = _buffer.Slice(_position, length);
        _position += length;

        // Truncate at null character if present
        int nullIdx = slice.IndexOf((byte)0);
        if (nullIdx >= 0)
        {
            slice = slice[..nullIdx];
        }

        encoding ??= Encoding.Latin1;
        return encoding.GetString(slice);
    }

    public string ReadLengthPrefixedString(Encoding? encoding = null)
    {
        ushort length = ReadUInt16();
        if (length == 0)
        {
            return string.Empty;
        }

        EnsureAvailable(length);
        ReadOnlySpan<byte> slice = _buffer.Slice(_position, length);
        _position += length;

        encoding ??= Encoding.Latin1;
        return encoding.GetString(slice);
    }

    public string ReadNullTerminatedString(Encoding? encoding = null)
    {
        int start = _position;
        while (_position < _buffer.Length && _buffer[_position] != 0)
        {
            _position++;
        }

        if (_position >= _buffer.Length)
        {
            throw new PacketParseException("Null terminator not found for string in buffer.");
        }

        ReadOnlySpan<byte> slice = _buffer[start.._position];
        _position++; // consume null byte

        encoding ??= Encoding.Latin1;
        return encoding.GetString(slice);
    }

    private readonly void EnsureAvailable(int count)
    {
        if (Remaining < count)
        {
            throw new PacketParseException($"Buffer underflow. Required: {count} bytes, available: {Remaining} bytes (Position: {_position}, Total: {_buffer.Length}).");
        }
    }
}
