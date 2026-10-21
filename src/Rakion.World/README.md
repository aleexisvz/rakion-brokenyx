# Rakion.World

`Rakion.World` implementa la lógica de juego del World Server de Rakion: autenticación mediante Token de Sesión, bypass orgánico de GameGuard, gestión y persistencia de personajes, y soporte de sondeo UDP para NAT traversal / hole punching.

## Arquitectura y Componentes

### 1. `WorldServer` (`Rakion.World.Hosting`)
- Orquesta el ciclo de vida del servidor de mundo:
  - Servidor TCP en el puerto 40708 (control y sesiones de juego).
  - Servidor UDP 1 en el puerto 40708 (NAT hole punch probe 1).
  - Servidor UDP 2 en el puerto 40709 (NAT hole punch probe 2).
- Ambos protocolos conviven limpiamente en el puerto 40708 gracias al aislamiento nativo de sockets TCP y UDP en el stack de red del sistema operativo.

### 2. `WorldPacketHandler` (`Rakion.World.Handlers`)
- Manejador TCP para el protocolo de juego con cifrado AES-128-CBC (clave estática `1674492837493282` y vector IV nulo):
  - **Framing de Cifrado:**
    - Cliente -> Servidor: `[0..1] ushort Opcode`, `[2..3] ushort SequenceId`, `[4..] Payload bytes` (relleno PKCS7 a múltiplo de 16 bytes).
    - Servidor -> Cliente: `[0..1] ushort Opcode`, `[2..] Payload bytes` (sin Sequence ID, relleno PKCS7 a múltiplo de 16 bytes).
  - **`WorldLogin` (`0x000C`):** Procesa la credencial de autenticación inicial presentada por el cliente contra el ticket emitido por el Broker Server. Responde con `0x000C` conteniendo la lista de personajes serializados, 3 ranuras habilitadas (`UnlockedSlots = 3`), oro (offset 40) y cash (offset 48).
  - **`WorldLoginPart2` (`0x000D`):** Handshake secundario de inicialización de mundo.
  - **`SendGameGuard` (`0x0001`):** Responde con éxito (`0x0002`), eliminando la necesidad de emuladores GameGuard o DLLs anticheat legadas.
  - **`KeepAlive` (`0x000F`):** Pings periódicos de actividad en lobby y selección de personajes (`?SendAlive@IScavengerWorldNet`).
  - **`UdpSuccess` (`0x000E`):** Al concluir el sondeo UDP de hole punching, el cliente emite Opcode `0x000E`. El servidor confirma con la estructura de 13 bytes (`Result = 0`, `IP1`, `Port1`, `IP2`, `Port2`), cerrando el temporizador y el diálogo modal *"Waiting for response from the world."* de forma definitiva.
  - **`CharacterCreate` (`0x0012`):** Valida la disponibilidad del nombre, crea el nuevo personaje en base de datos y responde con `0x002B`.
  - **`CharacterDelete` (`0x0013`):** Verifica la clave de borrado y retira el personaje (`0x002D`), aceptando claves vacías o de bypass (`sirmaster`) sin fallar ante la falta de diálogo de clave en el cliente retail v258.
  - **`CharacterChangeBuddyName` (`0x0015`):** Notificación de actualización del nombre de personaje para el sistema de mensajería (Buddy system) emitida por el cliente tras la creación de un personaje. El servidor confirma con código 0 y el nombre del personaje, cerrando el diálogo modal *"Notice: change request of character's name for messenger"* y habilitando la interactividad de la interfaz.
  - **`CharacterSelect` (`0x0014`):** Selecciona el personaje activo y responde con `0x002E`, autorizando la transición a la pantalla de lobby/tutorial/canales.
  - **`CharacterTutorialClear` (`0x001A`):** Notificación emitida por el cliente tras completar el tutorial (`?SendCharacterTutorialClear@IScavengerWorldNet`).

### 3. `WorldUdpPacketHandler` (`Rakion.World.Handlers`)
- Procesa datagramas UDP enviados por el cliente al entrar a la selección de personaje (`CMenuSelectCharacter`):
  - Recibe `0x0201` en el puerto 40708 (UDP 1) y `0x0202` en el puerto 40709 (UDP 2).
  - Responde con el datagrama de 12 bytes reflejando la Cookie enviada por el cliente y las banderas de puerto correspondientes (`0` para UDP 1, `1` para UDP 2).
  - Esta respuesta le indica al cliente v258 que la conectividad hacia el mundo está abierta, preparando el envío de `0x000E` (`UdpSuccess`) y evitando el timeout de desconexión (error 777 / 415: *"Network connection is blocked"*).


