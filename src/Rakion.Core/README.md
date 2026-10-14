# Rakion.Core

`Rakion.Core` es la biblioteca fundacional del proyecto `rakion-brokenyx`. Contiene los contratos de dominio puros, definiciones de protocolo, serialización binaria de alto rendimiento y utilidades criptográficas para el ecosistema de Rakion v258.

## Responsabilidades y Alcance

- **Independiente de I/O:** No tiene dependencias de red (`Sockets`), almacenamiento (`MySQL`) ni infraestructura de hosting. Puede ser consumida de forma segura por cualquier capa.
- **Cero Asignaciones Innecesarias:** Las operaciones de lectura y escritura operan sobre `Span<byte>`, `ReadOnlySpan<byte>` y memoria agrupada (`ArrayPool<byte>`).
- **Endianness Nativo:** Toda la codificación numérica se procesa explícitamente en **Little-Endian**, respetando la arquitectura de la CPU x86 de los clientes de Softnyx.

## Contenido del Módulo

### 1. Enums (`Rakion.Core.Enums`)
- **`RakionOpcode`:** Opcodes observados en la ingeniería inversa del cliente y servidor:
  - `0xCDDF` (`ServerListRequest`): Petición de lista de servidores.
  - `0x0101` (`ServerListResponse`): Respuesta con los servidores disponibles.
  - `0x1310` (`Login`): Login estándar de v258 (`SV_AUTH_LOGIN`).
  - Opcodes de salas y canales (71 / `0x47`, 83 / `0x53`, 97 / `0x61`, 127 / `0x7F`, etc.).
- **`CharacterClass`:** Clases del juego (`Swordman = 0`, `Archer = 1`, `Blacksmith = 2`, `Mage = 3`, `Ninja = 4`).
- **`GameMode`:** Modos de combate (`SoloDeathmatch`, `TeamDeathmatch`, `LeaderWars`, `Stage`, `GolemWars`, `BossRaid`).
- **`ServerStatus`:** Estados de congestión (`Normal`, `Busy`, `Full`, `Offline`).
- **`ChannelType`**, **`TeamType`**, **`RoomSlotStatus`**, **`IpcCommand`**.

### 2. Protocolo Binario (`Rakion.Core.Protocol`)
- **`PacketHeader`:** Representa los 4 bytes iniciales de todo paquete TCP de Rakion:
  - `ushort TotalLength` (Longitud total del paquete incluyendo la cabecera).
  - `ushort Opcode` (Identificador del comando).
- **`PacketReader`:** `ref struct` que consume `ReadOnlySpan<byte>` y extrae tipos primitivos (`ReadUInt16`, `ReadInt32`, `ReadSingle`, `ReadFixedString`, `ReadLengthPrefixedString`, `ReadNullTerminatedString`) con comprobación estricta de límites.
- **`PacketWriter`:** Generador de paquetes con buffers elásticos (`ArrayPool<byte>`) que calcula y estampa automáticamente la cabecera de 4 bytes al finalizar el paquete (`Build()`).
- **`IPacket`:** Contrato para paquetes fuertemente tipados.
- **`PacketParseException`:** Excepción tipada para fallos de formato o desbordamiento de búfer.

### 3. Criptografía IPC (`Rakion.Core.Cryptography`)
- **`IpcCrypto`:** Algoritmo exacto de suma de comprobación de 1 byte (**BCRC**) y cifrado simétrico XOR descompilado del protocolo Broker-WorldServer de `BrokenServer.exe`.
