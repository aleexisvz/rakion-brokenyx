using Rakion.Core.Enums;
using Rakion.Core.Protocol;

namespace Rakion.Core.Packets;

/// <summary>
/// Paid request with an optional discount coupon: [u8 UseCoupon][u16 CouponSlot]. Shared by the stat reset (0x1B)
/// and the bag (0x32) purchases.
/// Evidence: Observed in engine.dll SendCharacterStateClear / SendInventoryBuyBag (u8 + u16) and RakionWorldServ.exe
/// 0x4208E0 / 0x4226B0, which read the slot only when the first byte is not 0 and accept coupons 11000..11999.
/// </summary>
public sealed record CouponRequestPacket(ClientOpcode RequestOpcode, bool UseCoupon, ushort CouponSlot = 0) : IPacket
{
    public ushort Opcode => (ushort)RequestOpcode;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(UseCoupon ? (byte)1 : (byte)0);
        writer.WriteUInt16(CouponSlot);
    }

    public static bool TryParse(ClientOpcode opcode, ReadOnlySpan<byte> span, out CouponRequestPacket packet)
    {
        packet = null!;
        if (span.Length < 1 || (span[0] != 0 && span.Length < 3))
        {
            return false;
        }

        packet = new CouponRequestPacket(opcode, span[0] != 0, span[0] != 0 ? (ushort)(span[1] | (span[2] << 8)) : (ushort)0);
        return true;
    }
}

/// <summary>
/// Stat reset answer: [u8 Result][u32 Cash][u16 LevelPoints][u16 PowerPoints][u8 N][N x u32 PresentIds].
/// Evidence: Observed in engine.dll 0x36195400 (reads every field whatever the result). The legacy database agent
/// fills the same fields (RakionWorldServ.exe 0x41441D); results 1 (not enough cash) and 2 (nothing to reset) are
/// Observed there, 0x14..0x16 are coupon refusals (0x40BD80). The present list is a legacy event lottery; this
/// server sends none.
/// </summary>
public sealed record CharacterStateClearResponsePacket(byte Result, uint Cash, ushort LevelPoints, ushort PowerPoints) : IPacket
{
    public const byte Success = 0;
    public const byte NotEnoughCash = 1;
    public const byte NothingToReset = 2;
    public const byte CouponRefused = 0x14;

    // The value collides with the GameGuard confirmation number (both directions share 0x1B); the client reads a
    // 0x1B it receives as this answer (engine.dll dispatcher entry 0x1B -> 0x36195400).
    public ushort Opcode => (ushort)ServerOpcode.GameGuard;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(Result);
        writer.WriteUInt32(Cash);
        writer.WriteUInt16(LevelPoints);
        writer.WriteUInt16(PowerPoints);
        writer.WriteByte(0);
    }
}

/// <summary>
/// Answer to an account upgrade purchase (bag 0x32, potion slot 0x6F):
/// [u8 Result][u32 Gold][u32 Cash][u8 NewCount][u8 N][N x u32 PresentIds].
/// Evidence: Observed in engine.dll 0x361957C0 (0x32) and 0x36195AC0 (0x6F, fields read only for result 0). The order
/// gold then cash is Inferred from the shop answer (0x31). Result 3 (bag, not enough cash) is Observed at
/// RakionWorldServ.exe 0x415055 and 4 (potion slot) at 0x4179A8; <see cref="AtMaximum"/> is Inferred.
/// </summary>
public sealed record AccountUpgradeResponsePacket(ServerOpcode ResponseOpcode, byte Result, uint Gold, uint Cash, byte NewCount) : IPacket
{
    public const byte Success = 0;
    public const byte AtMaximum = 2;
    public const byte BagNotEnoughCash = 3;
    public const byte PotionSlotNotEnoughMoney = 4;

    public ushort Opcode => (ushort)ResponseOpcode;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(Result);
        writer.WriteUInt32(Gold);
        writer.WriteUInt32(Cash);
        writer.WriteByte(NewCount);
        writer.WriteByte(0);
    }
}

/// <summary>
/// Answer to a Stage purchase (rank clear 0x70, level pass 0x71): [u8 Result][u32 Gold][u32 Cash][u8 N][N x u32 PresentIds].
/// Evidence: Observed in engine.dll 0x36195BD0 and 0x36195CC0 (fields read only for result 0; unlike 0x6F there is
/// no count byte). Results 2 (not enough cash) and 3 (level pass still active) are Observed in the legacy agent
/// (RakionWorldServ.exe 0x41863B and 0x418728); 1 is its generic failure.
/// </summary>
public sealed record StagePurchaseResponsePacket(ServerOpcode ResponseOpcode, byte Result, uint Gold, uint Cash) : IPacket
{
    public const byte Success = 0;
    public const byte Failed = 1;
    public const byte NotEnoughCash = 2;
    public const byte StillActive = 3;

    public ushort Opcode => (ushort)ResponseOpcode;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(Result);
        writer.WriteUInt32(Gold);
        writer.WriteUInt32(Cash);
        writer.WriteByte(0);
    }
}
