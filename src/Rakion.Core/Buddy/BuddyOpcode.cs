namespace Rakion.Core.Buddy;

/// <summary>
/// Command codes of the Messenger (Buddy) protocol spoken by the client's Buddy2.dll on its own port (BuddyPort in
/// DataSetup/Locale.ini, 8500). Requests (SVC) are even, answers and notices (RET / NTF) odd.
/// Evidence: the names appear as strings in Buddy2.dll and the values in its builders (for example 0x1010 pushed at
/// 0x100079B2, Observed); the rest of the table is Inferred until each code is used.
/// </summary>
public enum BuddyOpcode : ushort
{
    PreCredentialRequest = 0x1000,
    PreCredentialResponse = 0x1001,
    LoginRequest = 0x1010,
    LoginResponse = 0x1011,
    VipIpPortNotice = 0x101F,
    CloseConnectionNotice = 0x1FFF,
    TunnelRequest = 0x2020,
    UserStateRequest = 0x3010,
    TunnelNotice = 0x2021,
    AddBuddyRequest = 0x3000,
    AddBuddyResponse = 0x3001,
    RemoveBuddyRequest = 0x3002,
    RemoveBuddyResponse = 0x3003,
    SetNickRequest = 0x3100,
    SetNickResponse = 0x3101,
    SetGuildRequest = 0x3102,
    SetGuildResponse = 0x3103,
    SetExtUserRequest = 0x3104,
    SetExtUserResponse = 0x3105,
    GroupListRequest = 0x3150,
    GroupListResponse = 0x3151,
    GroupAddRequest = 0x3152,
    GroupAddResponse = 0x3153,
    UserStateNotice = 0x3FFF,
}
