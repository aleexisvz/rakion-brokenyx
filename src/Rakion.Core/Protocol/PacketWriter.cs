using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace Rakion.Core.Protocol;

/// <summary>
/// High-performance expandable binary writer for building Rakion packets.
/// Emulates the insertion semantics of CNetMessage / PacketWriter.
/// Automatically prefixes the 4-byte header (Length + Opcode) when initialized with an opcode.
/// </summary>
public sealed class PacketWriter : IDisposable
{
    private const int DefaultInitialCapacity = 256;
    private byte[] _buffer;
    private int _position;
    private readonly bool _hasHeader;
    private readonly ushort _opcode;
    private bool _isDisposed;

    public PacketWriter(ushort opcode, int initialCapacity = DefaultInitialCapacity)
    {
        _opcode = opcode;
        _hasHeader = true;
        _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(initialCapacity, PacketHeader.HeaderSize));
        // Reserve 4 bytes for header
        _position = PacketHeader.HeaderSize;
    }

    public PacketWriter(int initialCapacity = DefaultInitialCapacity)
    {
        _opcode = 0;
        _hasHeader = false;
        _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(initialCapacity, 16));
        _position = 0;
    }

    public int Length => _position;
    public ushort Opcode => _opcode;
    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _position);

    public void WriteByte(byte value)
    {
        EnsureCapacity(sizeof(byte));
        _buffer[_position++] = value;
    }

    public void WriteBoolean(bool value) => WriteByte((byte)(value ? 1 : 0));

    public void WriteInt16(short value)
    {
        EnsureCapacity(sizeof(short));
        BinaryPrimitives.WriteInt16LittleEndian(_buffer.AsSpan(_position, sizeof(short)), value);
        _position += sizeof(short);
    }

    public void WriteUInt16(ushort value)
    {
        EnsureCapacity(sizeof(ushort));
        BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(_position, sizeof(ushort)), value);
        _position += sizeof(ushort);
    }

    public void WriteInt32(int value)
    {
        EnsureCapacity(sizeof(int));
        BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(_position, sizeof(int)), value);
        _position += sizeof(int);
    }

    public void WriteUInt32(uint value)
    {
        EnsureCapacity(sizeof(uint));
        BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan(_position, sizeof(uint)), value);
        _position += sizeof(uint);
    }

    public void WriteInt64(long value)
    {
        EnsureCapacity(sizeof(long));
        BinaryPrimitives.WriteInt64LittleEndian(_buffer.AsSpan(_position, sizeof(long)), value);
        _position += sizeof(long);
    }

    public void WriteUInt64(ulong value)
    {
        EnsureCapacity(sizeof(ulong));
        BinaryPrimitives.WriteUInt64LittleEndian(_buffer.AsSpan(_position, sizeof(ulong)), value);
        _position += sizeof(ulong);
    }

    public void WriteSingle(float value)
    {
        EnsureCapacity(sizeof(float));
        BinaryPrimitives.WriteSingleLittleEndian(_buffer.AsSpan(_position, sizeof(float)), value);
        _position += sizeof(float);
    }

    public void WriteBytes(ReadOnlySpan<byte> bytes)
    {
        EnsureCapacity(bytes.Length);
        bytes.CopyTo(_buffer.AsSpan(_position));
        _position += bytes.Length;
    }

    public void WriteZeroBytes(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count == 0) return;
        EnsureCapacity(count);
        _buffer.AsSpan(_position, count).Clear();
        _position += count;
    }

    public void WriteFixedString(string value, int fixedLength, Encoding? encoding = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfNegative(fixedLength);

        encoding ??= Encoding.Latin1;
        EnsureCapacity(fixedLength);

        Span<byte> target = _buffer.AsSpan(_position, fixedLength);
        target.Clear(); // Fill with zeroes

        int bytesWritten = encoding.GetBytes(value.AsSpan(), target);
        _position += fixedLength;
    }

    public void WriteLengthPrefixedString(string value, Encoding? encoding = null)
    {
        ArgumentNullException.ThrowIfNull(value);

        encoding ??= Encoding.Latin1;
        int byteCount = encoding.GetByteCount(value);
        if (byteCount > ushort.MaxValue)
        {
            throw new ArgumentException("String byte length exceeds ushort.MaxValue.", nameof(value));
        }

        WriteUInt16((ushort)byteCount);
        EnsureCapacity(byteCount);
        encoding.GetBytes(value.AsSpan(), _buffer.AsSpan(_position, byteCount));
        _position += byteCount;
    }

    public void WriteNullTerminatedString(string value, Encoding? encoding = null)
    {
        ArgumentNullException.ThrowIfNull(value);

        encoding ??= Encoding.Latin1;
        int byteCount = encoding.GetByteCount(value);
        EnsureCapacity(byteCount + 1);

        encoding.GetBytes(value.AsSpan(), _buffer.AsSpan(_position, byteCount));
        _position += byteCount;
        _buffer[_position++] = 0; // null terminator
    }

    /// <summary>
    /// Finalizes the packet header (if enabled) and returns a newly allocated byte array with the packet content.
    /// </summary>
    public byte[] Build()
    {
        FinalizeHeader();
        return _buffer.AsSpan(0, _position).ToArray();
    }

    /// <summary>
    /// Finalizes the packet header (if enabled) and copies the result into the destination buffer.
    /// </summary>
    public int CopyTo(Span<byte> destination)
    {
        FinalizeHeader();
        if (destination.Length < _position)
        {
            throw new ArgumentException("Destination span is too small.", nameof(destination));
        }

        _buffer.AsSpan(0, _position).CopyTo(destination);
        return _position;
    }

    private void FinalizeHeader()
    {
        if (_hasHeader)
        {
            if (_position > ushort.MaxValue)
            {
                throw new InvalidOperationException($"Packet size {_position} exceeds maximum allowable ushort payload length.");
            }

            BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(0, 2), (ushort)_position);
            BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(2, 2), _opcode);
        }
    }

    private void EnsureCapacity(int bytesNeeded)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (_position + bytesNeeded <= _buffer.Length)
        {
            return;
        }

        int newCapacity = Math.Max(_buffer.Length * 2, _position + bytesNeeded);
        byte[] newBuffer = ArrayPool<byte>.Shared.Rent(newCapacity);
        _buffer.AsSpan(0, _position).CopyTo(newBuffer);
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = newBuffer;
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = [];
            _position = 0;
        }
    }
}
