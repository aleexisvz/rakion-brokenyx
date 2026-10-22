using Rakion.Core.Protocol;

namespace Rakion.World.Session;

/// <summary>
/// Sends a typed packet to one World session, applying the session's wire framing
/// (AES-128 when the client negotiated it). Contract C-2 of the roadmap.
/// </summary>
public interface ISessionSender
{
    ValueTask SendAsync(IWorldSessionView session, IPacket packet, CancellationToken cancellationToken);
}
