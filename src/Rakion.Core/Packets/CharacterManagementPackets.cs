using Rakion.Core.Enums;
using Rakion.Core.Protocol;

namespace Rakion.Core.Packets;

/// <summary>
/// Character deletion request sent by client on opcode 0x0013.
/// Evidence: Observed in engine.dll ?SendCharacterDelete@IScavengerWorldNet@@UAEXKPAD@Z.
/// </summary>
public sealed record CharacterDeleteRequestPacket(
    uint CharacterId,
    string DeleteKey) : IPacket
{
    public ushort Opcode => (ushort)RakionOpcode.CharacterDelete;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteUInt32(CharacterId);
        writer.WriteNullTerminatedString(DeleteKey);
    }

    public static bool TryParse(ReadOnlySpan<byte> payload, out CharacterDeleteRequestPacket packet)
    {
        try
        {
            var reader = new PacketReader(payload);
            uint charId = reader.ReadUInt32();
            string key = reader.Remaining > 0 ? reader.ReadNullTerminatedString() : string.Empty;

            packet = new CharacterDeleteRequestPacket(charId, key);
            return true;
        }
        catch (PacketParseException)
        {
            packet = default!;
            return false;
        }
    }
}

/// <summary>
/// Character deletion confirmation response sent by server on opcode 0x002D.
/// Evidence: Observed in RakionWorldServ opcode 19 handler (0x002D).
/// </summary>
public sealed record CharacterDeleteResponsePacket(
    byte ResultCode,
    uint CharacterId) : IPacket
{
    public ushort Opcode => (ushort)RakionOpcode.CharacterDeleteResponse;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(ResultCode);
        writer.WriteUInt32(CharacterId);
    }

    public static bool TryParse(ReadOnlySpan<byte> payload, out CharacterDeleteResponsePacket packet)
    {
        if (payload.Length < 5)
        {
            packet = default!;
            return false;
        }

        try
        {
            var reader = new PacketReader(payload);
            byte result = reader.ReadByte();
            uint charId = reader.ReadUInt32();

            packet = new CharacterDeleteResponsePacket(result, charId);
            return true;
        }
        catch (PacketParseException)
        {
            packet = default!;
            return false;
        }
    }
}

/// <summary>
/// Character selection request sent by client on opcode 0x0014.
/// Evidence: Observed in engine.dll ?SendCharacterSelect@IScavengerWorldNet@@UAEXK@Z.
/// </summary>
public sealed record CharacterSelectRequestPacket(
    uint CharacterId) : IPacket
{
    public ushort Opcode => (ushort)RakionOpcode.CharacterSelect;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteUInt32(CharacterId);
    }

    public static bool TryParse(ReadOnlySpan<byte> payload, out CharacterSelectRequestPacket packet)
    {
        if (payload.Length < 4)
        {
            packet = default!;
            return false;
        }

        try
        {
            var reader = new PacketReader(payload);
            uint charId = reader.ReadUInt32();

            packet = new CharacterSelectRequestPacket(charId);
            return true;
        }
        catch (PacketParseException)
        {
            packet = default!;
            return false;
        }
    }
}

/// <summary>
/// Character selection confirmation response sent by server on opcode 0x002E.
/// Evidence: Observed in RakionWorldServ opcode 20 handler (0x002E).
/// </summary>
public sealed record CharacterSelectResponsePacket(
    byte ResultCode,
    uint CharacterId) : IPacket
{
    public ushort Opcode => (ushort)RakionOpcode.CharacterSelectResponse;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(ResultCode);
        writer.WriteUInt32(CharacterId);
    }

    public static bool TryParse(ReadOnlySpan<byte> payload, out CharacterSelectResponsePacket packet)
    {
        if (payload.Length < 5)
        {
            packet = default!;
            return false;
        }

        try
        {
            var reader = new PacketReader(payload);
            byte result = reader.ReadByte();
            uint charId = reader.ReadUInt32();

            packet = new CharacterSelectResponsePacket(result, charId);
            return true;
        }
        catch (PacketParseException)
        {
            packet = default!;
            return false;
        }
    }
}

