using Rakion.Core.Cryptography;
using Rakion.Core.Protocol;

namespace Rakion.World.Session;

/// <summary>
/// Default <see cref="ISessionSender"/>: serializes the packet and encrypts it when the
/// session uses the AES-128 World framing.
/// </summary>
public sealed class WorldSessionSender : ISessionSender
{
    public static WorldSessionSender Instance { get; } = new();

    public async ValueTask SendAsync(IWorldSessionView session, IPacket packet, CancellationToken cancellationToken)
    {
        if (session is not WorldSession worldSession)
        {
            throw new ArgumentException("Session view was not created by the World server.", nameof(session));
        }

        using var writer = new PacketWriter(packet.Opcode);
        packet.Serialize(writer);

        if (worldSession.IsEncrypted)
        {
            byte[] rawWire = writer.Build();
            ReadOnlySpan<byte> payloadSpan = rawWire.Length >= PacketHeader.HeaderSize
                ? rawWire.AsSpan(PacketHeader.HeaderSize)
                : [];

            byte[] encryptedWire = RakionWorldCrypto.EncryptServerPacket(packet.Opcode, payloadSpan);
            await worldSession.Connection.SendAsync(encryptedWire, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await worldSession.Connection.SendAsync(writer.Build(), cancellationToken).ConfigureAwait(false);
        }
    }
}
