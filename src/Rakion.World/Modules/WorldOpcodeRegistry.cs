using Rakion.Core.Enums;

namespace Rakion.World.Modules;

/// <summary>
/// Explicit, typed binding of client opcodes to handlers. Each opcode has exactly one owner.
/// </summary>
public sealed class WorldOpcodeRegistry
{
    private readonly Dictionary<ClientOpcode, WorldOpcodeHandler> _handlers = [];

    public int Count => _handlers.Count;

    public void Map(ClientOpcode opcode, WorldOpcodeHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (!_handlers.TryAdd(opcode, handler))
        {
            throw new InvalidOperationException(
                $"Opcode {opcode} (0x{(ushort)opcode:X4}) is already bound to another World handler.");
        }
    }

    public bool TryGetHandler(ushort opcode, out WorldOpcodeHandler handler) =>
        _handlers.TryGetValue((ClientOpcode)opcode, out handler!);

    public bool IsMapped(ClientOpcode opcode) => _handlers.ContainsKey(opcode);
}
