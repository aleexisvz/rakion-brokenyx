using Rakion.World.Session;

namespace Rakion.World.Modules;

/// <summary>
/// Handles one decrypted client packet on the World TCP connection.
/// The payload excludes the opcode and sequence id.
/// </summary>
public delegate ValueTask WorldOpcodeHandler(
    WorldSession session,
    ReadOnlyMemory<byte> payload,
    CancellationToken cancellationToken);
