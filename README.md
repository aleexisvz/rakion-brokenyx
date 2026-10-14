# rakion-brokenyx

Clean-room C# (.NET 10) server emulator for the **Rakion: Chaos Force v258**
client. No binary patches, no legacy DLLs — the protocol implemented natively.

> **Status:** Phase 0 (Core & Network Foundation) Completed.

## Documentation

- [AGENTS.md](AGENTS.md) — contribution rules and Git workflow
- [docs/ROADMAP.md](docs/ROADMAP.md) — phased implementation roadmap
- [docs/ECOSISTEMA_ARQUITECTURA.md](docs/ECOSISTEMA_ARQUITECTURA.md) — ecosystem architecture specification
- [docs/DESGLOSE.md](docs/DESGLOSE.md) — legacy server reverse-engineering report

## Project Modules

| Module | Description | Documentation |
| :--- | :--- | :--- |
| **`Rakion.Core`** | Opcodes, domain enums, 4-byte `PacketHeader`, `PacketReader`/`PacketWriter` (Little-Endian, zero-allocation), and IPC cryptography (BCRC/XOR). | [Rakion.Core/README.md](src/Rakion.Core/README.md) |
| **`Rakion.Network`** | High-performance asynchronous TCP/UDP socket listeners, connection framing with `System.IO.Pipelines`, and lifecycle management. | [Rakion.Network/README.md](src/Rakion.Network/README.md) |

## Building & Testing

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
# Build the entire solution
dotnet build

# Run all unit and integration test suites
dotnet test
```

## Disclaimer

Educational / preservation project. This repository contains no Softnyx
binaries, client assets or proprietary code. Rakion is a trademark of its
respective owners.
