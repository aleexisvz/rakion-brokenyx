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

### 3. Criptografía (`Rakion.Core.Cryptography`)
- **`RakionWorldCrypto`:** Cifrado simétrico AES-128-CBC utilizado en el World Server v258:
  - Clave estática de 16 bytes: `1674492837493282` (`31 36 37 34 34 39 32 38 33 37 34 39 33 32 38 32`).
  - Vector de inicialización (IV) de 16 bytes nulos.
  - Enmarcado Cliente -> Servidor: `[0..1] ushort Opcode`, `[2..3] ushort SequenceId`, `[4..] Payload bytes` (relleno PKCS7 a múltiplo de 16 bytes).
  - Enmarcado Servidor -> Cliente: `[0..1] ushort Opcode`, `[2..] Payload bytes` (sin Sequence ID, relleno PKCS7 a múltiplo de 16 bytes).
- **`IpcCrypto`:** Algoritmo exacto de suma de comprobación de 1 byte (**BCRC**) y cifrado simétrico XOR descompilado del protocolo Broker-WorldServer de `BrokenServer.exe`.

### 4. Paquetes del Protocolo (`Rakion.Core.Packets`)
- **Handshake y Auth:** `LoginRequestPacket`, `LoginResponsePacket`, `ServerListResponsePacket`, `WorldLoginRequestPacket`, `WorldLoginResponsePacket`, `WorldLoginPart2Packet`.
- **Sondeo UDP y NAT:** `UdpProbePacket` (0x0201 / 0x0202), `UdpSuccessPacket` (0x000E).
- **Gestión de Personajes:** `CharacterCreateRequestPacket` (0x0012), `CharacterCreateResponsePacket` (0x002B), `CharacterDeleteRequestPacket` (0x0013), `CharacterDeleteResponsePacket` (0x002D), `CharacterSelectRequestPacket` (0x0014), `CharacterSelectResponsePacket` (0x002E).
- **Seguridad:** `GameGuardPacket` (0x0001 / 0x0002).

### 5. Utilidades (`Rakion.Core.Utilities`)
- **`XfsPacker`:** Empacador de archivos `.xfs` con cabecera dual y compresión ZLIB, empleado para la generación dinámica o extracción de `NyxLauncherEnc.xfs`.
