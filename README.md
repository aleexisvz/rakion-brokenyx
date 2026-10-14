# rakion-brokenyx

Clean-room C# (.NET 10) server emulator for the **Rakion: Chaos Force v258**
client. No binary patches, no legacy DLLs — the protocol implemented natively.

> **Status:** Phase 0, Phase 1 & Phase 2 Completed. Phase 3 (World Server & Character Selection) Ready.

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
| **`Rakion.Database`** | Relational persistence with MySQL 8+/MariaDB, Dapper micro-ORM, InnoDB ACID transactions, and BCrypt password security with legacy migration. | [Rakion.Database/README.md](src/Rakion.Database/README.md) |
| **`Rakion.Broker`** | TCP Broker on port 40706 (server list, authentication, session ticket issuing) and Softnyx AutoFetch mini HTTP server. | [Rakion.Broker](src/Rakion.Broker/) |
| **`Rakion.World`** | World Server TCP on port 40708 (session handshake, organic GameGuard bypass, character list, creation, deletion, selection). | [Rakion.World](src/Rakion.World/) |
| **`Rakion.App`** | Unified console host running Broker (40706), World (40708), and AutoFetch (8080) concurrently via `Microsoft.Extensions.Hosting`. | [Rakion.App](src/Rakion.App/) |

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
