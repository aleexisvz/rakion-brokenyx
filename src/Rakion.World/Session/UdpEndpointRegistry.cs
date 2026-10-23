using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Rakion.World.Session;

/// <summary>
/// The two UDP endpoints of a player as the client and its room peers read them: (u32 ip, u16 port) twice.
/// Evidence: Observed as user + 0x1450/0x1458 and + 0x1454/0x145A in RakionWorldServ.exe (getter 0x40ABE0, used by
/// the room list host block and the room player record). Inferred: they are the endpoints the client reported in its
/// UDP probes to the two World UDP ports, kept in the client's own encoding.
/// </summary>
public readonly record struct UdpEndpoints(uint Ip1, ushort Port1, uint Ip2, ushort Port2);

/// <summary>
/// Gives each logged-in session the (u16 index, u32 key) pair the client echoes in its UDP probes, and stores the
/// endpoints those probes report.
/// Evidence: Observed in RakionWorldServ.exe 0x425D80: the probe's u16 selects the user slot and its u32 must match
/// the user's key (+0x1464). The client copies both from WorldLogin bytes [5..6] and [7..10] (Observed: the probes
/// echoed exactly those bytes).
/// </summary>
public sealed class UdpEndpointRegistry
{
    private readonly object _sync = new();
    private readonly ConcurrentDictionary<ushort, Entry> _entries = new();
    private ushort _next;

    public (ushort Index, uint Key) Register()
    {
        uint key = (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);
        lock (_sync)
        {
            for (int attempt = 0; attempt < ushort.MaxValue; attempt++)
            {
                _next = (ushort)(_next == ushort.MaxValue ? 1 : _next + 1);
                if (_entries.TryAdd(_next, new Entry(key)))
                {
                    return (_next, key);
                }
            }
        }

        throw new InvalidOperationException("No free UDP user index.");
    }

    public void Release(ushort index) => _entries.TryRemove(index, out _);

    /// <summary>
    /// Stores the endpoint of a probe to World UDP port 1 or 2, found by its key; ignored for an unknown key.
    /// The probe's u16 index is not used: WorldLogin bytes [5..6] stay 0 because a non-zero value kept the client
    /// from leaving character select (Observed in a client test). Superseded: that came from ChannelEnter carrying 0
    /// as the entering user's id; World now sends the index in both, and the key alone still identifies the probe.
    /// </summary>
    public bool TryRecord(uint key, bool secondPort, uint ip, ushort port)
    {
        var entry = _entries.Values.FirstOrDefault(e => e.Key == key);
        if (entry is null)
        {
            return false;
        }

        lock (entry)
        {
            entry.Endpoints = secondPort
                ? entry.Endpoints with { Ip2 = ip, Port2 = port }
                : entry.Endpoints with { Ip1 = ip, Port1 = port };
        }

        return true;
    }

    public UdpEndpoints Get(ushort index) =>
        _entries.TryGetValue(index, out var entry) ? entry.Endpoints : default;

    private sealed class Entry(uint key)
    {
        public uint Key { get; } = key;

        public UdpEndpoints Endpoints { get; set; }
    }
}
