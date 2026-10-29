# rakion-brokenyx

Clean-room C# (.NET 10) server emulator for the **Rakion: Chaos Force v258**
client. No binary patches, no legacy DLLs — the protocol implemented natively.

> **Status:** Phases 0–3 and 5 completed; most of phases 4, 6 and 7 done and verified with the retail v258 client (economy, blacksmith, rooms, matches in every mode including Golem War and Stage, rewards and levels, kick votes, GM commands, rankings, presents, potion use and stacking, character slots). Messenger server integrated (login, presence, friend list delivery); adding friends from the client is postponed (blocked by the v258 client). Pending: messenger messages, clans, server relay for public networks and Docker. See the roadmap.

## Documentation

- [AGENTS.md](AGENTS.md) — contribution rules and Git workflow
- [docs/ROADMAP.md](docs/ROADMAP.md) — phased implementation roadmap, work lanes and shared Core contracts
- [docs/ECOSISTEMA_ARQUITECTURA.md](docs/ECOSISTEMA_ARQUITECTURA.md) — ecosystem architecture specification
- [docs/DESGLOSE.md](docs/DESGLOSE.md) — legacy server reverse-engineering report

## Project Modules

| Module | Description | Documentation |
| :--- | :--- | :--- |
| **`Rakion.Core`** | Opcodes, domain enums, 4-byte `PacketHeader`, `PacketReader`/`PacketWriter` (Little-Endian, zero-allocation), AES-128-CBC protocol crypto, XFS packer, and IPC cryptography (BCRC/XOR). | [Rakion.Core/README.md](src/Rakion.Core/README.md) |
| **`Rakion.Network`** | High-performance asynchronous TCP/UDP socket listeners (`System.IO.Pipelines`), dual UDP hole punching servers, and lifecycle management. | [Rakion.Network/README.md](src/Rakion.Network/README.md) |
| **`Rakion.Database`** | Relational persistence with MySQL 8+/MariaDB, Dapper micro-ORM, InnoDB ACID transactions, and BCrypt password security with legacy migration. | [Rakion.Database/README.md](src/Rakion.Database/README.md) |
| **`Rakion.Broker`** | TCP Broker on port 40706 (server list, authentication, session ticket issuing) and Softnyx AutoFetch mini HTTP server. | [Rakion.Broker/README.md](src/Rakion.Broker/README.md) |
| **`Rakion.World`** | World Server TCP on port 40708 and dual UDP listeners (40708/40709): characters, lobby channels and chat, economy (inventory, shop, blacksmith, stat points and resets, PowerUser), rooms, matches, rewards, kick votes and GM commands. | [Rakion.World/README.md](src/Rakion.World/README.md) |
| **`Rakion.Relay`** *(planned, lane B)* | Socketless UDP relay routing (field-slot endpoint registry, tunneling `0x0401`) consumed by World's UDP listeners. | — |
| **`Rakion.Maintenance`** | Scheduled jobs hosted by the App: ranking recalculation (replaces `RankUpdate.exe`); anti-abuse checks pending. | [Rakion.Maintenance/README.md](src/Rakion.Maintenance/README.md) |
| **`Rakion.Buddy`** | Messenger server for the client's `Buddy2.dll` on TCP/UDP 8500: login, presence, friend list, tunnel and groups. Adding friends from the client is postponed. | [Rakion.Buddy/README.md](src/Rakion.Buddy/README.md) |
| **`Rakion.App`** | Unified console host running Broker (40706), World (TCP 40708 + UDP 40708/40709), Messenger (TCP/UDP 8500), AutoFetch (8080) and the maintenance jobs via `Microsoft.Extensions.Hosting`. Also the import tools (`--import-items`, `--import-levels`) and `--create-user`. | [Rakion.App](src/Rakion.App/) |

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
