namespace Rakion.Broker.Services;

/// <summary>
/// Information stored with an active session ticket issued during Broker login.
/// </summary>
public readonly record struct SessionTicketInfo(
    uint Ticket,
    string Username,
    string ClientIp,
    DateTime CreatedAtUtc);

/// <summary>
/// Contract for issuing and validating ephemeral session tickets between Broker and World servers.
/// </summary>
public interface ISessionTicketService
{
    /// <summary>
    /// Issues a new unique session ticket for the authenticated user.
    /// </summary>
    uint IssueTicket(string username, string clientIp);

    /// <summary>
    /// Looks up active ticket information without invalidating it.
    /// </summary>
    bool TryGetTicket(uint ticket, out SessionTicketInfo info);

    /// <summary>
    /// Looks up active ticket information by username without invalidating it.
    /// </summary>
    bool TryGetTicketByUsername(string username, out SessionTicketInfo info);

    /// <summary>
    /// Looks up the most recent active ticket information by client IP or loopback without invalidating it.
    /// </summary>
    bool TryGetTicketByClientIp(string clientIp, out SessionTicketInfo info);

    /// <summary>
    /// Validates the ticket and consumes it (one-time use for World server handshake).
    /// </summary>
    bool ValidateAndConsumeTicket(uint ticket, out SessionTicketInfo info);
}
