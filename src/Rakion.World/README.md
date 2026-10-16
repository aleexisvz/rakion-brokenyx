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
- Manejador TCP para el protocolo de juego:
  - **`SendGameGuard` (`0x0001`):** Responde con éxito (`0x0002`), eliminando la necesidad de emuladores GameGuard o DLLs anticheat legadas.
  - **`SendSessionTicket` (`0x0007`):** Valida el ticket emitido por el Broker Server y asocia el socket con la cuenta de usuario.
  - **`SendCharacterList` (`0x000B`):** Carga los personajes del jugador desde la base de datos y los envía empaquetados en `0x000C`.
  - **`SendCharacterCreate` (`0x000D`):** Valida la disponibilidad del nombre, crea el nuevo personaje en base de datos y le asigna el equipamiento inicial.
  - **`SendCharacterDelete` (`0x000F`):** Verifica la clave de borrado y retira el personaje.
  - **`SendCharacterSelect` (`0x0011`):** Selecciona el personaje activo y autoriza la transición a la pantalla de lobby/canales.

### 3. `WorldUdpPacketHandler` (`Rakion.World.Handlers`)
- Procesa datagramas UDP enviados por el cliente al entrar a la selección de personaje (`CMenuSelectCharacter`):
  - Recibe `0x0201` en el puerto 40708 (UDP 1) y `0x0202` en el puerto 40709 (UDP 2).
  - Responde con el datagrama de 12 bytes reflejando la Cookie enviada por el cliente y las banderas de puerto correspondientes (`0` para UDP 1, `1` para UDP 2).
  - Esta respuesta le indica al cliente v258 que la conectividad hacia el mundo está abierta, cerrando el diálogo modal *"Waiting for response from the world."* y evitando el timeout de desconexión (error 777: *"Network connection to server is blocked"*).
