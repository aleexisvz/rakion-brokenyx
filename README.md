# rakion-brokenyx

Clean-room C# (.NET 10) server emulator for the **Rakion: Chaos Force v258**
client. No binary patches, no legacy DLLs — the protocol implemented natively.

> **Status:** Phase 0, Phase 1 & Phase 2 Completed (100%). Phase 3 (World Server & Character Management) Items 3.1–3.5 Completed & Verified in-game with Retail Client v258.

## Documentation

- [AGENTS.md](AGENTS.md) — contribution rules and Git workflow
- [docs/ROADMAP.md](docs/ROADMAP.md) — phased implementation roadmap
- [docs/ECOSISTEMA_ARQUITECTURA.md](docs/ECOSISTEMA_ARQUITECTURA.md) — ecosystem architecture specification
- [docs/DESGLOSE.md](docs/DESGLOSE.md) — legacy server reverse-engineering report

## Project Modules

| Module | Description | Documentation |
| :--- | :--- | :--- |
| **`Rakion.Core`** | Opcodes, domain enums, 4-byte `PacketHeader`, `PacketReader`/`PacketWriter` (Little-Endian, zero-allocation), AES-128-CBC protocol crypto, XFS packer, and IPC cryptography (BCRC/XOR). | [Rakion.Core/README.md](src/Rakion.Core/README.md) |
| **`Rakion.Network`** | High-performance asynchronous TCP/UDP socket listeners (`System.IO.Pipelines`), dual UDP hole punching servers, and lifecycle management. | [Rakion.Network/README.md](src/Rakion.Network/README.md) |
| **`Rakion.Database`** | Relational persistence with MySQL 8+/MariaDB, Dapper micro-ORM, InnoDB ACID transactions, and BCrypt password security with legacy migration. | [Rakion.Database/README.md](src/Rakion.Database/README.md) |
| **`Rakion.Broker`** | TCP Broker on port 40706 (server list, authentication, session ticket issuing) and Softnyx AutoFetch mini HTTP server. | [Rakion.Broker/README.md](src/Rakion.Broker/README.md) |
| **`Rakion.World`** | World Server TCP on port 40708 (AES-128 crypto, session handshake, organic GameGuard bypass, 3-slot character list, creation, deletion, selection, tutorial completion) and Dual UDP listeners (40708/40709) for NAT hole punch probe. | [Rakion.World/README.md](src/Rakion.World/README.md) |
| **`Rakion.App`** | Unified console host running Broker (40706), World (TCP 40708 + UDP 40708/40709), and AutoFetch (8080) concurrently via `Microsoft.Extensions.Hosting`. | [Rakion.App](src/Rakion.App/) |

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
