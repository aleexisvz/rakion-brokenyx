using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Rakion.Broker.Services;

/// <summary>
/// Thread-safe in-memory session ticket management service.
/// </summary>
public sealed class SessionTicketService : ISessionTicketService
{
    private static readonly TimeSpan TicketLifetime = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<uint, SessionTicketInfo> _tickets = new();

    public uint IssueTicket(string username, string clientIp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        // Purge expired tickets periodically and invalidate prior tickets for this user
        PurgeExpired();

        foreach (var (oldTicket, item) in _tickets)
        {
            if (string.Equals(item.Username, username, StringComparison.OrdinalIgnoreCase))
            {
                _tickets.TryRemove(oldTicket, out _);
            }
        }

        uint ticket;
        do
        {
            ticket = RandomNumberGenerator.GetInt32(1, int.MaxValue) is var val ? (uint)val : 1;
        } while (_tickets.ContainsKey(ticket));

        var info = new SessionTicketInfo(ticket, username, clientIp, DateTime.UtcNow);
        _tickets[ticket] = info;
        return ticket;
    }

    public bool TryGetTicket(uint ticket, out SessionTicketInfo info)
    {
        if (_tickets.TryGetValue(ticket, out info))
        {
            if (DateTime.UtcNow - info.CreatedAtUtc <= TicketLifetime)
            {
                return true;
            }

            _tickets.TryRemove(ticket, out _);
        }

        info = default;
        return false;
    }

    public bool TryGetTicketByUsername(string username, out SessionTicketInfo info)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        DateTime now = DateTime.UtcNow;
        foreach (var (ticket, item) in _tickets)
        {
            if (string.Equals(item.Username, username, StringComparison.OrdinalIgnoreCase))
            {
                if (now - item.CreatedAtUtc <= TicketLifetime)
                {
                    info = item;
                    return true;
                }

                _tickets.TryRemove(ticket, out _);
            }
        }

        info = default;
        return false;
    }

    public bool TryGetTicketByClientIp(string clientIp, out SessionTicketInfo info)
    {
        DateTime now = DateTime.UtcNow;
        SessionTicketInfo best = default;
        bool found = false;

        foreach (var (ticket, item) in _tickets)
        {
            bool matches = string.Equals(item.ClientIp, clientIp, StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(clientIp, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(clientIp, "::1", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(item.ClientIp, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(item.ClientIp, "::1", StringComparison.OrdinalIgnoreCase);

            if (matches)
            {
                if (now - item.CreatedAtUtc <= TicketLifetime)
                {
                    if (!found || item.CreatedAtUtc > best.CreatedAtUtc)
                    {
                        best = item;
                        found = true;
                    }
                }
                else
                {
                    _tickets.TryRemove(ticket, out _);
                }
            }
        }

        info = best;
        return found;
    }

    public bool ValidateAndConsumeTicket(uint ticket, out SessionTicketInfo info)
    {
        if (_tickets.TryRemove(ticket, out info))
        {
            if (DateTime.UtcNow - info.CreatedAtUtc <= TicketLifetime)
            {
                return true;
            }
        }

        info = default;
        return false;
    }

    private void PurgeExpired()
    {
        DateTime now = DateTime.UtcNow;
        foreach (var (ticket, info) in _tickets)
        {
            if (now - info.CreatedAtUtc > TicketLifetime)
            {
                _tickets.TryRemove(ticket, out _);
            }
        }
    }
}
