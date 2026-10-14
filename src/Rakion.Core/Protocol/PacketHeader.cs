namespace Rakion.Core.Protocol;

/// <summary>
/// Fixed 4-byte header preceding every Rakion TCP network packet.
/// Structure:
///   [0..1] ushort TotalLength (including 4-byte header)
///   [2..3] ushort Opcode
/// </summary>
public readonly record struct PacketHeader(ushort TotalLength, ushort Opcode)
{
    public const int HeaderSize = 4;

    /// <summary>
    /// Gets the length of the payload excluding the 4-byte header.
    /// </summary>
    public int PayloadLength => TotalLength >= HeaderSize ? TotalLength - HeaderSize : 0;

    /// <summary>
    /// Attempts to parse a header from a 4-byte span.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> buffer, out PacketHeader header)
    {
        if (buffer.Length < HeaderSize)
        {
            header = default;
            return false;
        }

        ushort length = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(buffer[..2]);
        ushort opcode = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(buffer[2..4]);

        header = new PacketHeader(length, opcode);
        return true;
    }

    /// <summary>
    /// Writes the header to the target span.
    /// </summary>
    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < HeaderSize)
        {
            throw new ArgumentException("Destination buffer is too small for PacketHeader.", nameof(destination));
        }

        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(destination[..2], TotalLength);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(destination[2..4], Opcode);
    }
}
