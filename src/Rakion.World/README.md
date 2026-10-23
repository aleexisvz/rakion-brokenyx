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
  - **`SendGameGuard` (`0x0010`):** Responde con éxito (`0x001B`), eliminando la necesidad de emuladores GameGuard o DLLs anticheat legadas.
  - **`KeepAlive` (`0x000F`):** Pings periódicos de actividad en lobby y selección de personajes (`?SendAlive@IScavengerWorldNet`).
  - **`UdpSuccess` (`0x000E`):** Al concluir el sondeo UDP de hole punching, el cliente emite Opcode `0x000E`. El servidor confirma con la estructura de 13 bytes (`Result = 0`, `IP1`, `Port1`, `IP2`, `Port2`), cerrando el temporizador y el diálogo modal *"Waiting for response from the world."* de forma definitiva.
  - **`CharacterCreate` (`0x0012`):** Valida la disponibilidad del nombre, crea el nuevo personaje en base de datos y responde con `0x0012`.
  - **`CharacterDelete` (`0x0013`):** Verifica la clave de borrado y retira el personaje (respuesta `0x0013`; el valor legacy de RakionWorldServ era `0x002D`), aceptando claves vacías o de bypass (`sirmaster`) sin fallar ante la falta de diálogo de clave en el cliente retail v258.
  - **`CharacterChangeBuddyName` (`0x0015`):** Notificación de actualización del nombre de personaje para el sistema de mensajería (Buddy system) emitida por el cliente tras la creación de un personaje. El servidor confirma con código 0 y el nombre del personaje, cerrando el diálogo modal *"Notice: change request of character's name for messenger"* y habilitando la interactividad de la interfaz.
  - **`CharacterSelect` (`0x0014`):** Selecciona el personaje activo y responde con `0x0014` (legacy `0x002E`), autorizando la transición a la pantalla de lobby/tutorial/canales.
  - **`CharacterTutorialClear` (`0x001A`):** Notificación emitida por el cliente tras completar el tutorial (`?SendCharacterTutorialClear@IScavengerWorldNet`).
  - **`ChannelList` (`0x001D`):** Retorna la lista de canales activos en el servidor (Canal Principiante, Canal Libre, Canal de Clanes) con su tipo, concurrencia actual y capacidad máxima.
  - **`ChannelEnter` (`0x001F`):** Procesa la entrada del jugador a un canal, validando restricciones de nivel (ej. nivel máximo 15 en Principiante) y capacidad de jugadores.
  - **`ChannelExit` (`0x0020`):** Procesa la salida limpia del canal activo y confirma la desvinculación de sesión.
  - **`ChannelCharacters` (`0x001E`):** Entrega la lista de personajes presentes en el lobby del canal actual.
  - **`ChannelChat` (`0x0022`):** Recibe mensajes de chat de lobby y los difunde a todas las sesiones activas en el mismo canal (tolerante al formato cliente que antepone `"{Personaje} : "`).
  - **`CharacterWhisper` (`0x0016`):** Enrutamiento bidireccional de susurros (mensajes privados) entre personajes conectados en el servidor.
  - **`FieldList` (`0x0036`):** Solicitud periódica o al entrar al canal para obtener las salas activas. Responde con `FieldListResponsePacket` con paginación real o `.Empty` cuando no hay salas creadas, cerrando el diálogo modal de espera (*"Waiting for game list from the world."*) y habilitando la navegación del Lobby.
  - **`FieldCreate` (`0x003B`):** Creación de sala por el host (`?SendFieldCreate`). Valida parámetros, instancia `GameField`, asigna la ranura 0 al host y confirma con `0x003B` (`ResultCode = 0`, `RoomNumber`), permitiendo el ingreso inmediato a la sala de espera.
  - **`FieldQuickEnter` (`0x0039`):** Ingreso rápido a la primera sala disponible del canal sin contraseña ni partida en curso; si no hay ninguna responde `0x0039` sin datos.
  - **`FieldEnter` (`0x0038`):** Ingreso a sala por número y contraseña. Al entrar, todos los miembros reciben `0x0038` con el registro del nuevo jugador (nombre, nivel, equipo y sus direcciones UDP) y el que entra recibe `0x0037` con las reglas y los 20 slots. Errores con los códigos del original: 1 no disponible, 2 llena, 3 contraseña, 6 en partida, 7 nivel.
  - **`FieldExit` (`0x003A`):** Salida limpia de la sala de espera hacia el lobby del canal, transfiriendo el liderazgo si sale el host o destruyendo la sala si queda vacía. Si el cliente se desconecta, o crea o entra en otra sala sin salir, el servidor lo saca solo para no dejar salas fantasma.
  - **`FieldKick` (`0x0040`):** El host expulsa a un miembro; el expulsado vuelve al lobby como en una salida normal.
  - **`FieldReady` (`0x003D`):** Conmutación del estado Listo (Ready) difundido a todos los miembros de la sala.
  - **`FieldChat` (`0x0047`):** Chat dentro de la sala, difundido como `[u8 slot][texto]`.
  - **`FieldSlotUdp` (`0x0062`):** Comprobación de red entre jugadores al entrar: el servidor la reenvía al slot indicado con el slot del remitente.
  - **Partida:** `FieldGameStart` (`0x0043`), `FieldGameEnter` (`0x0045`), `FieldGameExit` (`0x0046`, difunde el slot del que sale), `FieldGameRoundStart` (`0x0048`, cliente cargado), `FieldGameAddPlayer` / `Reply` (`0x004B` / `0x004C`, reenviados como `0x004B`) y `FieldGameDie` (`0x004F`). Ver la sección 7.
  - **`FieldChangeSlotStatus` (`0x0042`):** Apertura o bloqueo/cierre de ranuras por el host (`?SendFieldChangeSlotStatus`). Difunde `0x0042` (`SlotIndex`, `Status`), cerrando el popup modal *"Changing Status"*.
  - **`FieldChangeTeam` (`0x003E`):** Cambio de equipo entre Rojo (ranuras 0..9) y Azul (ranuras 10..19) (`?SendFieldChangeTeam`). Busca una ranura libre no cerrada en el equipo opuesto y difunde `0x003E` (`Result`, `OldSlot`, `NewSlot`).
  - **`FieldChangeRule` (`0x0041`):** Modificación de mapa, modo y reglas de sala por el anfitrión (`?SendFieldChangeRule`), difundido a los miembros con `0x0041`.

### 3. Registro de opcodes (`Rakion.World.Modules`, contrato C-1)
- `WorldPacketHandler` ya no usa un `switch`: descifra el paquete y busca el handler en `WorldOpcodeRegistry`, un mapa explícito y tipado `ClientOpcode → WorldOpcodeHandler`.
- Los handlers propios de World se registran en `RegisterBuiltInHandlers()`. Otras funcionalidades (por ejemplo la economía del carril A, en `Features/Economy/`) implementan `IWorldOpcodeModule` y se registran en DI; no editan `WorldPacketHandler`.
- Cada opcode tiene un único dueño: si un módulo intenta mapear un opcode ya enlazado, el constructor del handler lanza `InvalidOperationException` al arrancar.

### 4. Vista de sesión y envío (`Rakion.World.Session`, contrato C-2)
- `IWorldSessionView`: lectura de la sesión para funcionalidades de otros carriles (usuario, personaje activo, canal, `IsInField`). `WorldSession` la implementa de forma explícita; los consumidores no modifican la sesión.
- `ISessionSender` / `WorldSessionSender`: serializa un `IPacket` y lo cifra con AES-128 si la sesión negoció el framing cifrado. `WorldPacketHandler` envía todas sus respuestas a través de él.
- `IsInField` indica si la sesión está en una sala.
- `UdpEndpointRegistry`: da a cada sesión un índice (bytes 5–6 del login, el id propio del cliente, que también viaja en `ChannelEnter` y en los registros de la sala) y una clave (bytes 7–10). El cliente repite ambos en sus sondeos UDP, y el registro guarda las direcciones que reportan para que los jugadores de una sala se conecten entre sí.

### 5. `ChannelManager` (`Rakion.World.Channels`)
- Gestor concurrente en memoria de canales de juego (`LobbyChannel`).
- Provee aislamiento de salas de chat y lista de usuarios por canal con soporte para restricciones por nivel y límites de jugadores.

### 6. `WorldUdpPacketHandler` (`Rakion.World.Handlers`)
- Procesa datagramas UDP enviados por el cliente al entrar a la selección de personaje (`CMenuSelectCharacter`):
  - Recibe `0x0201` en el puerto 40708 (UDP 1) y `0x0202` en el puerto 40709 (UDP 2).
  - Responde con el datagrama de 12 bytes reflejando la Cookie enviada por el cliente y las banderas de puerto correspondientes (`0` para UDP 1, `1` para UDP 2).
  - Esta respuesta le indica al cliente v258 que la conectividad hacia el mundo está abierta, preparando el envío de `0x000E` (`UdpSuccess`) y evitando el timeout de desconexión (error 777 / 415: *"Network connection is blocked"*).
  - Guarda en `UdpEndpointRegistry` la dirección que reporta cada sondeo, identificada por la clave del login.

### 7. Salas y partidas (`Rakion.World.Fields`)
- `FieldManager` / `GameField`: salas en memoria por canal, 20 slots (0–9 equipo rojo, 10–19 azul). `FieldRules` guarda las reglas con el significado del servidor original: el modo es el segundo byte de la petición de crear; también el tiempo límite, el objetivo de kills, el número de rondas y el rango de niveles.
- `GameField.Match.cs`: flujo de la partida según `RakionWorldServ.exe` (0x408440, 0x4087D0, 0x409A00):
  - La ronda empieza cuando todos los jugadores han cargado el mapa (`0x0048`), con tiempo límite + 3 s.
  - Puntuación por modo: 2 individual (kills del asesino, +2 con la causa 8, −1 con la causa 1), 3 por equipos, 4 líderes (la ronda acaba si muere un líder). Los modos 0 y 1 solo avisan de la muerte.
  - Fin de ronda (`0x004A`) por objetivo o por tiempo, pausa de 15 s, siguiente ronda (`0x0049`) y fin de partida (`0x0044`: 2 última ronda, 5 equipo vacío, 6 pocos jugadores).
- `WorldServer` ejecuta cada segundo `WorldPacketHandler.TickFieldsAsync` para los plazos de las rondas.
- Votación de expulsión en partida (`0x005D` abrir `[objetivo][motivo]`, `0x005E` votar `[1 sí / 2 no / 3 abstención]`; `RakionWorldServ.exe` 0x40A420 / 0x4098E0 / 0x409810 / 0x4090B0, **Observed**): hacen falta 3 jugadores cargados, no se puede votar contra el dueño ni contra uno mismo, dura 60 s o hasta que votan todos menos el objetivo; se expulsa si los sí superan a los no y sí + no llegan a la mitad. Resultado y rechazos por `0x005F`. En el cliente se abre con `/kick <slot> <motivo>` y se vota con `/1`, `/2` o `/3` (`rakion.bin` 0x40DF40, **Observed**). Si el objetivo se va, se cancela. La lista temporal de expulsados del original (no poder volver a entrar) no está implementada.
- Cerrar sala (`0x003F`): solo GM (autoridad 100 o más, equivalente **Inferred** de la autoridad 0x34 del original, 0x423C00); saca a todos al lobby. El cliente v258 no tiene comando ni botón que envíe `0x003F` (solo un envoltorio sin uso, `rakion.bin` 0x408C50, **Inferred**).
- Stage (modo 0): el cliente del dueño de la sala cierra la ronda con `0x004A` `[motivo]` (2 superado, 3 fallado; `RakionWorldServ.exe` 0x405A90); morir todo el equipo también la pierde. Cada jugador envía `0x0053` con stage, rango, hasta 4 drops, EXP, oro y EXP de criaturas: solo si el rango mejora el guardado (`user_stage_info`, 0x40BAE0) se guardan rango, drops (primer hueco libre del box, **Inferred**) y recompensa (cuenta empate) en una transacción, y se responde `0x0053` `[0][stage][rango][drops]`. Los rangos viajan en la ficha de cada personaje.
- Recompensas (`0x0050`, `RakionWorldServ.exe` 0x424B60): el cliente calcula la EXP y el oro de cada ronda; el servidor solo los acepta tras un fin de ronda, con topes por modo (por encima se descarta el informe: «Wrong Game Point!»), suma 50 % de EXP a los PowerUser y guarda todo con `IMatchResultRepository` (contrato C-4). La subida de nivel usa la tabla `class_level_info` (importada del `DataSetup.xfs` del cliente con `--import-levels <ruta>`; la del dump es de otra versión: 70/188 frente a 47/133), da 3 puntos por nivel y se avisa con `0x0051`. En la misma transacción se cuenta victoria/derrota/empate (el servidor decide según el ganador de la ronda, `0x405980`; en modos solo cuenta empate) y se suma la EXP de criatura (máx. 100 por ronda) a las criaturas equipadas en los slots 10–12, que suben de nivel con la tabla `creature_level_info` (legacy `NPCInfo`, importada con `--import-items`). Si cambia algún nivel, la sala recibe `0x0052` `[slot][nivel][3 × nivel de criatura]`.
- Pendiente: relevo por servidor (`0x0056` / `0x0057`, `0x0401`), golems y jefes, EXP de criaturas y victorias/derrotas del informe `0x0050`, votaciones y cerrar sala.


