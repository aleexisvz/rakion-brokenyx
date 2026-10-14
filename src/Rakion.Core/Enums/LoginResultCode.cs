namespace Rakion.Core.Enums;

/// <summary>
/// Status codes returned to the Rakion client during authentication handshake.
/// Evidence: Observed in BrokenServer IPC_INFO_LOGIN and RakionWorldServ DBCommandLogin.
/// </summary>
public enum LoginResultCode : ushort
{
    /// <summary>
    /// Authentication successful.
    /// </summary>
    Success = 0,

    /// <summary>
    /// Invalid username or password.
    /// </summary>
    InvalidCredentials = 1,

    /// <summary>
    /// Account is currently banned or suspended.
    /// </summary>
    AccountBanned = 2,

    /// <summary>
    /// The target game server or lobby is full.
    /// </summary>
    ServerFull = 3,

    /// <summary>
    /// General server error or database unavailable.
    /// </summary>
    ServerError = 4,

    /// <summary>
    /// Account is already logged in on another session.
    /// </summary>
    AlreadyLoggedIn = 5,
}
