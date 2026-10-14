namespace Rakion.Core.Protocol;

/// <summary>
/// Defines a typed Rakion network packet.
/// </summary>
public interface IPacket
{
    /// <summary>
    /// Gets the unique opcode associated with this packet.
    /// </summary>
    ushort Opcode { get; }

    /// <summary>
    /// Serializes the packet payload using the provided PacketWriter.
    /// </summary>
    void Serialize(PacketWriter writer);
}
