namespace Rakion.World.Modules;

/// <summary>
/// A feature that owns a set of client opcodes on the World TCP connection
/// (contract C-1 of the roadmap). Modules are registered in DI and bound once at startup;
/// mapping an opcode that is already bound fails fast.
/// </summary>
public interface IWorldOpcodeModule
{
    void Register(WorldOpcodeRegistry registry);
}
