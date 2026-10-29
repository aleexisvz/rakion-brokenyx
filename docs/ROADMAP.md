# Hoja de Ruta (Roadmap): Emulador Rakion Server en C# (.NET 10 LTS)
**Proyecto:** `rakion-brokenyx`  
**Directorio Base:** `C:\Users\Alexis\Projects\C#\rakion-brokenyx`  
**Filosofía del Proyecto:** Construir un servidor limpio, modular, de alto rendimiento y de código abierto en C# que emule de forma nativa y orgánica los protocolos de Rakion v258. Cero parches binarios, cero inyecciones de memoria, cero dependencias obsoletas de 32-bit (GameGuard/MySQL 4) y soporte nativo multiplataforma (Windows y Linux/Docker).

---

## Organización del Trabajo: Dos Carriles

Dos agentes trabajan en paralelo, cada uno en su propio `git worktree` (`..\rakion-claude` y
`..\rakion-gemini`). Nadie edita archivos en la carpeta del otro agente.

| Carril | Dueño | Rutas | Fases |
| :--- | :--- | :--- | :--- |
| **A — Persistencia y economía** | Claude | `src/Rakion.Database/`, `src/Rakion.Broker/`, `src/Rakion.World/Features/Economy/`, `src/Rakion.Maintenance/` y sus tests | 4 y 7 |
| **B — Mundo en tiempo real** | Gemini | `src/Rakion.World/` (excepto `Features/Economy/`), `src/Rakion.Relay/`, `src/Rakion.Network/` y sus tests | 5 y 6 |
| **Compartido** | Un único dueño a la vez (ver regla 6) | `src/Rakion.Core/`, `src/Rakion.App/`, `config/appsettings.json`, `RakionBrokenyx.sln`, `Directory.Build.props` | Contratos |
| **Integrador** | Mantenedor | Checkboxes de este archivo, `README.md` raíz, `AGENTS.md` | — |

### Contratos Compartidos

Cada contrato se entrega en un PR pequeño que se mergea antes de que el otro carril lo consuma.

| ID | Contrato | Dueño | Consumidor | Bloquea |
| :--- | :--- | :--- | :--- | :--- |
| **C-0** | Separar `RakionOpcode` en `ClientOpcode` (C→S) y `ServerOpcode` (S→C) y eliminar alias con valores repetidos, sin cambiar bytes en el cable. | B | A, B | 4.1, 5.x |
| **C-1** | `IWorldOpcodeModule`: registro tipado `opcode → handler` en World; `WorldPacketHandler` delega en él los opcodes que no maneja. | B | A (`EconomyModule`) | 4.1 |
| **C-2** | `IWorldSessionView` (usuario, personaje, canal, sala, en partida) + `ISessionSender` (enviar a una sesión). | B | A | 4.1, 7.3, 7.4 |
| **C-3** | Paquetes de inventario, tienda, herrería y estadísticas en `Rakion.Core/Packets/`. | A | B | 4.x |
| **C-4** | `IMatchResultRepository` en Database: persistir EXP, oro, victorias/derrotas y EXP de criaturas en una transacción. | A | B | 6.4 |
| **C-5** | `IChatCommandInterceptor`: el chat de canal y de sala lo consulta antes de difundir. | B | A | 7.3 |
| **C-6** | `IWorldTelemetry`: sesiones, salas y partidas activas. | B | A | 7.4 |

> **Estado de los contratos:** C-0 a C-5 entregados (C-5 en #35). C-6 pendiente (7.4).

### Reglas de Integración

1. Cada carril actualiza el README de sus módulos. Los checkboxes de este archivo solo los marca el integrador al mergear.
2. Cada PR declara qué paquetes y contratos de `Rakion.Core` usa o modifica.
3. Cada paquete tiene un test de round-trip (serializar → bytes → parsear). Quien lo añade es su dueño; el otro carril lo consume sin reescribirlo.
4. Cada ítem lleva su etiqueta de evidencia y la marca "verificado con cliente real: sí/no".
5. Si un carril necesita un opcode o layout del otro, no lo implementa: lo pide al mantenedor y se coordina.
6. En `Rakion.Core`, cada carril añade sin pedir turno sus propios paquetes, en sus propios archivos (por ejemplo `FieldPackets.cs` para B e `InventoryPackets.cs` para A), y sus opcodes en su propia sección de `ClientOpcode` / `ServerOpcode`. Nadie modifica los paquetes ni los opcodes del otro carril. Para el resto de rutas compartidas (`PacketReader`/`PacketWriter`, `PacketHeader`, criptografía, `Rakion.App/Program.cs`, `config/appsettings.json`, `.sln`, `Directory.Build.props`), el carril lo anuncia al mantenedor y el otro no las toca hasta que ese PR se mergee.

### Resolución del Solapamiento UDP

Los listeners UDP 40708/40709 ya existen en `Rakion.World` desde el ítem 3.1 y el mapa
cookie → sesión vive allí. Por eso los sockets se quedan en World, y `Rakion.Relay` nace
como librería sin sockets con la lógica de relevo (registro de endpoints por slot,
tunneling `0x0401`, limitación de tasa). `WorldUdpPacketHandler` le despacha los paquetes.
Así no se enlaza el mismo puerto en dos sitios y Relay se prueba sin red.

---

## Estructura de la Solución Propuesta

```text
rakion-brokenyx/
├── src/
│   ├── Rakion.Core/             # Tipos primitivos, Opcodes, Enums, CNetMessage, Criptografía AES/BCRC
│   ├── Rakion.Network/          # Sockets asíncronos TCP (Pipelines) y UDP, Buffers de memoria
│   ├── Rakion.Database/         # Modelos de datos, Dapper, Repositorios MySQL/MariaDB
│   ├── Rakion.Broker/           # Servidor de Handshake, Login y Entrega de ServerList (Puerto 40706)
│   ├── Rakion.World/            # Servidor de Juego: Lobby, Salas, Inventario, Herrería (Puerto 40708 + UDP dual)
│   ├── Rakion.Relay/            # Lógica de relevo UDP P2P (sin sockets; los listeners 40708/40709 viven en World)
│   ├── Rakion.Maintenance/      # Rankings, antifraude y tareas programadas (fase 7)
│   ├── Rakion.Buddy/            # Servidor del Messenger (Buddy2.dll), TCP/UDP 8500
│   └── Rakion.App/              # Punto de entrada unificado / Daemon CLI para ejecutar todos los servicios
├── tests/
│   ├── Rakion.Core.Tests/       # Pruebas unitarias de serialización de paquetes, opcodes y AES-128
│   ├── Rakion.Network.Tests/    # Pruebas de integración de sockets y concurrencia TCP
│   ├── Rakion.Database.Tests/   # Pruebas de persistencia, concurrencia de balance y repositorios SQLite
│   ├── Rakion.Broker.Tests/     # Pruebas de autenticación Broker, tickets de sesión y AutoFetch HTTP
│   ├── Rakion.World.Tests/      # Pruebas de ciclo de vida de WorldServer, login y UDP hole punching
│   ├── Rakion.Maintenance.Tests/ # Pruebas del cálculo de rankings
│   └── Rakion.Buddy.Tests/      # Pruebas de extremo a extremo del Messenger (TCP/UDP)
├── config/
│   └── appsettings.json         # Configuración centralizada de IPs, puertos, base de datos y tasas de juego
└── docs/
    ├── ECOSISTEMA_ARQUITECTURA.md
    └── ROADMAP.md
```

---

## Fases de Implementación

### Fase 0: Cimientos de Red y Protocolo de Serialización
> **Objetivo:** Disponer de las herramientas fundamentales de serialización binaria y comunicación por sockets de alto rendimiento.
> **Estado:** Completada (100%).

- [x] **0.1 Estructura del Proyecto:** Crear la solución .NET 10 (`dotnet new sln`) con los proyectos modulares (`Rakion.Core`, `Rakion.Network`, `Rakion.Core.Tests`, `Rakion.Network.Tests`).
- [x] **0.2 Diccionario de Opcodes y Enums:**
  - Tipificar todos los Opcodes descubiertos (`0xCDDF`, `0x0101`, `0x1310`, `0x3000`, etc.) en un enum fuertemente tipado (`RakionOpcode`).
  - Crear enums para Clases (`CharacterClass`: 0=Swordman .. 4=Ninja), Modos de Juego (`GameMode`), Canales y Estados de Sala.
- [x] **0.3 Serializador Binario (`PacketReader` y `PacketWriter`):**
  - Implementar serialización Little-Endian con soporte para `Span<byte>` y `Memory<byte>` de cero asignaciones innecesarias de memoria.
  - Implementar los operadores de lectura/escritura idénticos a `CNetMessage` del cliente: `uint8`, `uint16`, `uint32`, cadenas prefijadas o terminadas en `\0`, y vectores/bloques de datos.
- [x] **0.4 Motor de Conexiones TCP Asíncronas:**
  - Crear una abstracción de listener TCP basada en `System.IO.Pipelines` (`TcpServer`, `TcpConnection`, `ITcpPacketHandler`) capaz de recibir múltiples conexiones de clientes simultáneas sin bloqueos y con enmarcado continuo de paquetes.
- [x] **0.5 Pruebas Unitarias de Red:** Validar que los paquetes serializados en C# coincidan byte por byte con los que el cliente de Rakion espera y validar enmarcado / fragmentación en TCP en tiempo de ejecución.

---

### Fase 1: Capa de Persistencia y Modelado de Datos
> **Objetivo:** Establecer la base de datos relacional limpia, sin los errores de diseño heredados del 2005.
> **Estado:** Completada (100%).

- [x] **1.1 Modelo de Entidades:**
  - Cuentas de usuario: `UserEntity` (ID, Password hash seguro BCrypt con fallback para contraseñas heredadas MD5/plaintext, Email, País, Autoridad GM).
  - Información de juego: `UserGameInfoEntity` y `CashEntity` (Oro, Cash, Ranuras de personajes desbloqueadas, Bolsas de inventario, PowerUser, Baneo).
  - Personajes: `CharacterEntity` (Nombre, Clase, Nivel, EXP, Puntos para repartir, Estadísticas `hit1`–`hit4`, `chit`, `hp`, `ap`, velocidad, CP, `deleteKey`).
  - Inventario e Items: `ItemEntity` (Catálogo maestro de armas, armaduras y criaturas) y `UserItemEntity` (Instancias de objetos de los jugadores con refinamiento de herrería y nivel de células).
  - Clanes y Amigos: `ClanEntity` y `BuddyEntity`.
- [x] **1.2 Migración de Base de Datos:**
  - Esquema DDL en `src/Rakion.Database/Migrations/schema.sql` compatible con MySQL 8.0 / MariaDB modernas (motor InnoDB para transacciones ACID reales, reemplazando tipos de datos deprecados como `datetime default '0000-00-00 00:00:00'`).
- [x] **1.3 Repositorios y Hasheo de Contraseñas:**
  - `IPasswordHasher` / `PasswordHasher` con BCrypt y detección de rehash automático de hashes heredados MD5 y contraseñas legadas.
  - Repositorios desacoplados con Dapper (`IUserRepository`, `ICharacterRepository`, `IInventoryRepository`).
  - `IAccountBalanceRepository`: Operaciones transaccionales atómicas (`WHERE gold >= @Amount`) que erradican exploits de duplicación (*duping*) y condiciones de carrera.
- [x] **1.4 Pruebas Automatizadas de Persistencia:**
  - Pruebas unitarias de hasheo y pruebas de integración en `tests/Rakion.Database.Tests` sobre SQLite en memoria validando transacciones de saldo, límites de slots, unicidad de personajes y CRUD de inventario.

---

### Fase 2: El Broker Server (Autenticación y Lista de Mundos)
> **Objetivo:** Lograr que el cliente de Rakion abra, se conecte al puerto 40706, muestre la lista de servidores y complete el inicio de sesión.
> **Estado:** Completada (100%).

- [x] **2.1 Servidor TCP en Puerto 40706:** Escuchar conexiones iniciales del cliente (`BrokerServer`, soporte multi-cliente concurrente con TCP asíncrono y framing de 4 bytes).
- [x] **2.2 Manejador del Opcode `0xCDDF` (ServerList Request):**
  - Responder con el paquete **`0x0101`** estructurado (`ServerListResponsePacket`):
    - Cantidad de mundos activos.
    - Por cada mundo: ID, Nombre (`WorldServer 1`), IP pública/privada dinámica (tomada de `appsettings.json`), Puerto en formato Big-Endian (40708), estado (`Normal/Lleno`), cupo máximo y jugadores en línea actuales.
  - **Resultado visual:** La pantalla de selección de servidores del cliente se dibuja correctamente con los servidores en verde.
- [x] **2.3 Manejador del Opcode `0x1310` / `0x3000` / `0x4000` (SV_AUTH_LOGIN):**
  - Desempaquetar `UserID`, `Password` y versión del cliente (`LoginRequestPacket`).
  - Autenticación dinámica de `NyxLauncher.exe`: descifrado AES-128-ECB de la clave estática `0x464FA0`, verificación de desafío de contraseña con clave de sesión `SHA-0` (`NyxLauncherCrypto`), y vinculación del usuario autenticado a la conexión TCP para que `0x4000` (nombrado `SV_AUTH_LOGIN_4` en `BrokenServer`, **Observed**; su papel de "Start Game" es **Inferred** del flujo del launcher) emita el ticket de sesión al usuario real.
  - Validar contra la base de datos (`IUserRepository`), con actualización automática transparente a BCrypt de contraseñas heredadas (MD5 / plaintext).
  - Comprobar estado de baneo en `user_game_info` (`is_banned`, `ban_date`, `ban_reason`).
  - Responder con código de éxito (`LoginResultCode.Success`) o error de credenciales/baneo (`LoginResponsePacket`).
  - Generar un Ticket o Token de Sesión criptográfico único en memoria con TTL de 5 minutos (`ISessionTicketService` / `SessionTicketService`).
- [x] **2.4 Endpoint Opcional AutoFetch (Sustituto de `fetch.php`):**
  - Servidor HTTP ligero interno (`AutoFetchServer`) emulando el protocolo Softnyx AutoFetch para `NyxLauncher.exe` (`fetch.php`), sirviendo URLs de avisos, parches y versión objetivo sin depender de servidores web externos.

---

### Fase 3: World Server (Gestión de Personajes, Canales y Lobby)
> **Objetivo:** Lograr que el cliente entre al juego en el puerto 40708, cargue sus personajes, permita crear/borrar y acceda al canal de Lobby.
> **Estado:** Completada (100%). Ítems 3.1 a 3.6 terminados con pruebas automatizadas. Verificado con cliente real: sí (3.1–3.6).

- [x] **3.1 Servidor TCP y Dual UDP en Puertos 40708/40709:** Listener principal TCP de sesiones de juego y listeners duales UDP para responder a sondeos de NAT traversal / keep-alive del cliente.
- [x] **3.2 Handshake de Sesión:** El cliente presenta su Token de Sesión obtenido del Broker y el WorldServer asocia el socket con la cuenta `CUser`.
- [x] **3.3 Bypass Orgánico de GameGuard:**
  - Cuando el cliente envíe su paquete de verificación (`SendGameGuard`), el servidor responde con la confirmación de validación correcta de forma transparente. Esto elimina para siempre la necesidad de DLLs `ggauth*.dll` o parches en el cliente.
- [x] **3.4 Listado y Selección de Personajes (`SendCharacterList` y `SendCharacterSelect`):**
  - Consultar personajes de la cuenta en `characterinfo` y serializar sus datos (Clase, Nivel, Modelo 3D, Estadísticas calculadas, Equipamiento visual actual).
  - Al seleccionar un personaje, colocarlo en estado activo de sesión.
- [x] **3.5 Creación y Borrado de Personajes:**
  - Implementar `SendCharacterCreate`: verificar que el nombre no esté en uso, crear fila con clase seleccionada (0 a 4) y otorgar el equipamiento básico inicial.
  - Implementar `SendCharacterDelete`: verificar la clave de borrado (`deletekey`) y retirar el personaje de forma segura ante claves vacías (comportamiento retail v258).
  - Implementar `SendCharacterChangeBuddyName` (`0x0015`): responder a la confirmación de nombre para el sistema de mensajería (Buddy system), cerrando el diálogo modal "Notice" post-creación.
- [x] **3.6 Sistema de Canales de Lobby:**
  - Implementar canales: Canal de Principiantes (con límite de nivel), Canal Libre, Canal de Clanes.
  - Procesar entrada (`SendChannelEnter`) y salida (`SendChannelExit`).
  - Chat general del canal (`SendChannelChat`) con difusión a todos los sockets presentes en el canal.
  - Mensajes privados / susurros (`SendCharacterWhisper`).

---

> **Leyenda de ítems (fases 4–7):** cada ítem declara su **Evidencia** (Observed / Derived /
> Inferred / Unknown, ver `AGENTS.md` §3) y si fue **verificado con cliente real** (sí/no).
> Un ítem solo se marca `[x]` cuando su PR está mergeado; el integrador lo marca al mergear.

### Fase 4: Economía, Inventario, Tienda y Herrería
> **Objetivo:** Permitir a los jugadores gestionar sus objetos, comprar en la tienda y mejorar equipamiento en la herrería.
> **Carril:** A. **Código:** `src/Rakion.World/Features/Economy/`, `src/Rakion.Database/`, paquetes C-3 en `Rakion.Core`.

> **Estado:** 4.0–4.9 mergeados (PRs #9–#21, #27, #30, #33, #41, #42; contratos #9–#11). Pendientes: los cupones de descuento (tabla **Unknown**). Detalle en `src/Rakion.World/Features/Economy/README.md`.

- [x] **4.0 Prerrequisitos:** contratos C-0 (#9), C-1 (#10) y C-2 (#11) mergeados en `main`; C-3 (paquetes de economía en `InventoryPackets.cs`) entregado con cada ítem.
- [x] **4.1 Carga y Envío de Inventario (`SendInventoryEnter`):** (#12, #13, #14, #15, #17)
  - Entrar/salir del inventario (`0x002C` / `0x002D`), el Box de la cuenta (120 ranuras, 30 por bolsa) en el login, el equipo de cada personaje (19 ranuras) y mover objetos entre Box y equipo (`0x0031`).
  - Evidencia: opcodes y layouts **Observed** (`engine.dll` 0x36193650 / 0x36193680 / 0x36193810 y `RakionWorldServ.exe`). Verificado con cliente real: sí.
- [x] **4.2 Tienda de Objetos (`SendInventoryBuy`, `0x002E`):** (#18, #21)
  - Precio validado contra el catálogo del servidor (importado con `--import-items`), compra atómica con log, tiendas solo oro / solo cash / ambas, ítems PowerUser y compra de la PU Card (`0x0034`).
  - Evidencia: layout de petición y respuesta **Observed** (`engine.dll` 0x36195640, `rakion.bin` 0x4774E0). Verificado con cliente real: sí.
- [x] **4.3 Venta y Movimiento de Items (`SendInventorySell`, `0x002F`; `SendInventoryMove`, `0x0031`):** (#16, #19)
  - Venta por oro (precio de oro × 0,4 o de cash × 1,5, **Observed** en el servidor original) y movimiento dentro del Box y hacia el equipo.
  - Verificado con cliente real: sí.
- [x] **4.4 Asignación de Puntos (`SendInventoryAllocationPoint(uint8 statIndex)`, `0x0033`):** (#20)
  - Reparto de puntos a las 10 estadísticas con tope 50 (**Inferred**); primero gasta los puntos de nivel y luego los de PowerUser.
  - Evidencia: layout **Observed** (`engine.dll` 0x361938A0, `rakion.bin` 0x47DBB0). Verificado con cliente real: sí.
- [x] **4.5 El Sistema de Herrería y Refinado (`SendEncahntReinforce`, `0x0074`):** (#27, #30, #43)
  - Comprobaciones del servidor original (equipo por debajo de +15, piedras 13001–13005) y resultados 0–8 **Observed** (`RakionWorldServ.exe` 0x40C310); nunca destruye el objeto (0x40C7A9).
  - Probabilidades del original (`RakionWorldServ.exe` 0x40C404–0x40C790, tabla por piedra en 0x41E6F4, **Observed**): éxito base y caída por nivel de cada piedra, más los catalizadores 14001 (rotura → sin cambio), 14002 y 14003 (Charm, +5 % de éxito seguro cada uno), hasta 3 y consumidos con la piedra. La probabilidad de bajar de nivel se sigue ajustando con `World:Rewards:EnchantDowngradeScale`.
  - Verificado con cliente real: sí.
- [x] **4.6 Regalos (`SendPresentPeek` / `SendPresentAccept`)** *(opcional)*: (#41)
  - Bandeja `pending_presents` (la `pendingpresents` del original): ver el más antiguo (`0x006B` → `[res][id][ítem][u16]`), aceptar a un slot vacío de la caja (`0x006C` → `[res][u32]`, 3 si el slot está ocupado) y descartar (`0x006D` → `[res]`). **Observed** (`RakionWorldServ.exe` 0x416BF0, 0x41710E; `engine.dll` 0x36194530–0x36194590).
  - El login lleva el número de regalos en el `u16` previo al oro (`AccountInfo+0x5C`); el cliente muestra el aviso y enciende el botón de recibir (`rakion.bin` 0x47E3EB, 0x47C3A7, 0x47355D, **Observed**).
  - Los GM envían regalos con `/present <pj> <ítem>`; más adelante, la web. Fuera: el regalo aleatorio de las compras en cash (tabla **Unknown**) y los regalos de oro.
  - Verificado con cliente real: sí.
- [x] **4.7 Reseteo de Puntos (`SendCharacterStateClear`, `0x001B`):** (#33)
  - Precio en cash por nivel (7000 / 12000 / 19000), resultados 1 (sin cash) y 2 (nada que resetear); el personaje conserva 3 puntos por nivel y el resto vuelve a los puntos PowerUser. **Observed** (`RakionWorldServ.exe` 0x413FD4, 0x41441D; cliente `engine.dll` 0x36195400); reparto **Derived**.
  - La respuesta va por `0x001B`, que es solo la respuesta del reseteo (ver 4.9).
  - Verificado con cliente real: sí.
- [x] **4.8 Otras compras y pociones:** (#33, #41)
  - Bolsa (`0x0032`, 8000 de cash, hasta 4 bolsas), slot de poción (`0x006F`, ítems 10008–10010 del catálogo) y cantidad de las pociones compradas (`hit2` del catálogo en `exp`). **Observed**.
  - Slot de personaje (`0x0035`): 12 000 de cash, hasta 6; resultado 3 en el máximo o sin cash (0x40B249, 0x415775, **Observed**).
  - Gastar pociones en partida (`0x006E` `[slot de equipo][ítem]`): baja 1 la cantidad del slot de equipo del personaje (0x428C90, 0x40BE4F, **Observed**).
  - Apilar pociones (`0x0073`): dos pociones con el mismo `hit4` se suman en el destino y el origen se borra (0x421AC1, 0x40C140, **Observed**).
  - Verificado con cliente real: sí. Fuera: cupones de descuento (11000–11999, tabla **Unknown**) y la regla de un uso por slot fuera de Stage (**Unknown**).
- [x] **4.9 Revisar la confirmación de GameGuard:** (#42)
  - El original no responde al `0x0010`: pasa los bytes a GameGuard y solo desconecta si la comprobación falla (`RakionWorldServ.exe` 0x41FC00 → 0x40A9C0, 0x41EB20, **Observed**). Ya no enviamos `0x001B`, que el cliente leía como la respuesta del reseteo de puntos (`engine.dll` 0x36195400).
  - Verificado con cliente real: sí.
  - Verificado con cliente real: no.

---

### Fase 5: Sistema de Salas (Matchmaking y Modos de Juego)
> **Objetivo:** Permitir la creación de salas de espera, cambio de equipos, selección de mapas y cuenta regresiva de inicio.
> **Carril:** B. **Código:** `src/Rakion.World/` (excepto `Features/Economy/`).

> **Estado:** Completada: 5.1–5.5 mergeados (PRs #22–#25, #34). Detalle en `src/Rakion.World/README.md`.

- [x] **5.1 Lista de Salas (`SendFieldList`, `0x0036`):** (#22, #24)
  - Entrada de la lista con el layout del cliente: 11 bytes de reglas, bloque del host (sus direcciones UDP) y nombre.
  - Evidencia: **Observed** (`RakionWorldServ.exe` 0x405790, `engine.dll` 0x36193900). Verificado con cliente real: sí.
- [x] **5.2 Creación de Sala (`SendFieldCreate`, `0x003B`):** (#22, #24)
  - El opcode real en v258 es `0x003B` (no `0x0053`). Reglas guardadas con el significado del servidor original en `FieldRules` (el modo es el segundo byte); slots iniciales cerrados como el original (6 por equipo en los modos 1–4).
  - Evidencia: **Observed** (`RakionWorldServ.exe` 0x405440 / 0x405568); nombres de los modos **Inferred**. Verificado con cliente real: sí.
- [x] **5.3 Sala de Espera (Lobby de Sala):** (#22, #24, #25)
  - Entrar (`0x0038`) y entrada rápida (`0x0039`) con el detalle de la sala (`0x0037`) y el aviso a los demás (`0x0038`); slots abiertos/cerrados (`0x0042`), cambio de equipo (`0x003E`), reglas (`0x0041`), listo (`0x003D`), chat (`0x0047`), salida (`0x003A`), comprobación de red entre jugadores (`0x0062`) y expulsar (`0x0040`).
  - Identidad de cada jugador: índice y clave en el login, el mismo índice en `ChannelEnter` y en la sala; direcciones UDP de cada jugador en sus registros.
  - Evidencia: **Observed** (`RakionWorldServ.exe` 0x406F40, 0x4073B7, 0x406930, 0x423CC0; `rakion.bin` 0x473DE5 / 0x47A170). Verificado con cliente real: sí (dos clientes).
- [x] **5.4 Inicio de Partida (`SendFieldGameStart`, `0x0043`):** (#23, #25)
  - Comprobaciones del original (equipos, todos listos), carga del mapa, entrar (`0x0045`) y salir (`0x0046`) de la partida.
  - Evidencia: **Observed** (`RakionWorldServ.exe` 0x4079D0, 0x407C70, 0x407E00). Verificado con cliente real: sí.
- [x] **5.5 Votaciones y cerrar sala:** (#34, #35)
  - Votación de expulsión en partida (`0x005D` / `0x005E`, resultado `0x005F`): 3 jugadores como mínimo, 60 s, gana el sí por mayoría con participación de la mitad. **Observed** (`RakionWorldServ.exe` 0x40A420, 0x4090B0). En el cliente: `/kick <slot> <motivo>` y `/1` `/2` `/3`.
  - Cerrar sala: solo GM. El cliente v258 no envía `0x003F`, así que el GM usa `/close` (7.3).
  - Verificado con cliente real: sí (votación con 3 jugadores).

---

### Fase 6: Combate en Tiempo Real y Relay UDP
> **Objetivo:** Permitir que los jugadores jueguen la partida de forma fluida, intercambien golpes y el servidor registre muertes, rondas y recompensas.
> **Carril:** B. **Código:** `src/Rakion.Relay/` (librería sin sockets), `src/Rakion.World/` (listeners UDP ya existentes desde 3.1). 6.4 consume el contrato C-4 del carril A.

> **Estado:** 6.1 y 6.3–6.7 mergeados (PRs #24, #25, #28–#30, #32, #40). El combate va de cliente a cliente por UDP y funciona con dos y tres clientes en la misma red. Pendiente: el relevo por servidor (6.2, para cuando se abra al público).

- [x] **6.1 Registro de Endpoints UDP por Jugador (`SendFieldSlotUDP`, `0x0062`):** (#24)
  - Los sondeos UDP llevan el índice y la clave del login; `UdpEndpointRegistry` guarda las direcciones de cada jugador y viajan en los registros de la sala para que los clientes se conecten entre sí. `0x0062` se reenvía entre miembros.
  - Evidencia: **Observed** (`RakionWorldServ.exe` 0x425D80, 0x40ABE0, 0x406930); que esos campos sean direcciones es **Inferred**. Verificado con cliente real: sí.
- [ ] **6.2 Mecanismo de Relevo (Relay / Tunneling):**
  - `SendFieldGameTunnelingAll` / `One` (`0x0056` / `0x0057`) por TCP y `0x0401` por UDP. El original solo lo activa cuando algún jugador no puede conectar directo (aviso `0x0054`).
  - Verificado con cliente real: no.
- [x] **6.3 Arbitraje de Eventos Críticos (muertes):** (#24, #25)
  - Inicio de ronda cuando todos cargan (`0x0048`), aparición de jugadores (`0x004B` / `0x004C` reenviados) y muertes (`0x004F`) con la puntuación de cada modo (individual, por equipos, líderes).
  - Evidencia: **Observed** (`RakionWorldServ.exe` 0x408440, 0x405C00, 0x4087D0). Verificado con cliente real: sí.
- [x] **6.4 Fin de Ronda y Partida (`0x004A`, `0x0049`, `0x0044`):** (#25)
  - Fin de ronda por objetivo o por tiempo, pausa de 15 s, siguiente ronda y fin de partida (última ronda o equipo vacío), con un temporizador de salas en `WorldServer`.
  - Evidencia: **Observed** (`RakionWorldServ.exe` 0x408E3E, 0x409A00). Verificado con cliente real: sí.
- [x] **6.5 Entidades maestras y modos de escenario:** (#28, #29, #32)
  - Golem War (modo 1): la ronda termina cuando muere un golem o todo un equipo; al acabar el tiempo gana el golem con más vida (`RakionWorldServ.exe` 0x405D70, 0x4063A0, **Observed**).
  - `0x0060` (`SendFieldGameMasterBossHP`): el original solo lo acepta de los líderes de Leader Wars (0x425CC0, **Observed**); se guarda para el desempate por tiempo.
  - Stage (modo 0): el dueño de la sala cierra la ronda (`0x004A`, 2 superado / 3 fallado) y cada jugador cobra con `0x0053` solo si mejora su rango del stage; rangos, drops y recompensa en una transacción (0x405A90, 0x425010, 0x40BAE0, **Observed**).
  - Verificado con cliente real: sí (Golem War y stages 1–2).
- [x] **6.7 Extras de Stage:** límites de nivel y de jugadores de cada stage (`StageInfo`) al crear la sala, y las compras `0x0070` (borrar rangos) y `0x0071` (acceso libre por nivel). (#40)
  - Tabla de 48 stages (`StageCatalog`, filas de `StageInfo` del original): la sala abre solo los slots del stage; crear fuera de rango responde 2 y unirse, 7 (0x4055C8, 0x41DB17, 0x406FEB, **Observed**).
  - `0x0070`: borra los rangos del personaje por el precio en cash de su franja de nivel (ítems 10011–10013, gratis por debajo de nivel 10). `0x0071`: acceso libre 24 h (ítem 10014), no se vende mientras sigue activo (resultado 3). Respuesta `[resultado][oro][cash][0]` (0x36195BD0 / 0x36195CC0, **Observed**).
  - El cliente salta el límite de nivel de la lista de stages por su cuenta: lo sabe por la hora de compra que va al final del bloque de cuenta del `0x000D` (`rakion.bin` 0x47E781 → 0x46553F, **Observed**). Tras comprar hay que volver a iniciar sesión.
  - Verificado con cliente real: sí.
  - Verificado con cliente real: no.
- [x] **6.6 Recompensas (`SendFieldGamePoint`, `0x0050`):** (#28, #29, #30)
  - El cliente calcula la EXP y el oro; el servidor aplica los topes por modo del original, +50 % de EXP a PowerUser y los multiplicadores `World:Rewards` (`RakionWorldServ.exe` 0x424B60, **Observed**). Persistencia con `IMatchResultRepository` (C-4).
  - Subida de nivel con la tabla del cliente (`--import-levels`, `DataSetup.xfs`), +3 puntos por nivel y aviso `0x0051`; victorias/derrotas/empates, EXP de criaturas (tabla `NPCInfo`) y aviso a la sala `0x0052`.
  - Verificado con cliente real: sí.

---

### Fase 7: Servicios de Mantenimiento, Rankings y Herramientas GM
> **Objetivo:** Dotar al ecosistema de herramientas de administración en vivo, sistema de rangos y detección de tramposos.
> **Carril:** A. **Código:** `src/Rakion.Maintenance/` (hospedado por `Rakion.App`), `src/Rakion.Database/`, `src/Rakion.World/Features/Admin/`.

> **Estado:** 7.1 y 7.3 mergeados (PRs #35, #36). Pendientes 7.2 y 7.4. Detalle en `src/Rakion.Maintenance/README.md` y `src/Rakion.World/README.md`.

- [x] **7.1 Motor de Rankings (Reemplazo de `RankUpdate.exe`):** (#36)
  - `RankingScheduler` (al arrancar y cada 24 h) y `/rankupdate`: puesto por país y por clase con empates, grados 26 → 15 por EXP y cupos 1 / 4 / 16 / porcentajes acumulados para los grados 14 → 1, cuentas activas en 2 meses, sin `auth` 2, 10 y 0x34. **Observed** (SQL de `RankUpdate.exe`, 0x402300, 0x402409).
  - Rangos en la ficha del personaje y tablas `ranking_total` / `ranking_class` para una futura web. Pendiente: ranking de clanes (cuando existan los clanes).
  - Verificado con cliente real: sí.
- [ ] **7.2 Módulo Antifraude y Detector de Abusers:**
  - Umbrales `CheckWinSum=200`, `CheckExpSum=6000`, `CheckGoldSum=12000` y consulta de agregación de 24 h: **Observed** (`Config.ini` y SQL de `RankUpdate.exe`).
  - Solo marca para revisión de un GM; no banea automáticamente.
  - Verificado con cliente real: no aplica (proceso batch).
- [x] **7.3 Comandos de Administrador en el Chat (In-Game GM Commands):** (#35)
  - `/notice` es nativo del cliente (`0x0005`, entregado por `0x0063`; **Observed** en 0x41F290 / 0x40C870). Comandos de **diseño propio** vía C-5: `/gm`, `/givegold`, `/givecash`, `/giveitem`, `/ban`, `/unban`, `/kickout`, `/close`, `/rankupdate`. Oro, cash e ítems en transacción y con log.
  - GM = autoridad 100 o más (**Inferred**, equivalente al 0x34 del original). Los avisos se recortan a 128 caracteres (más largos tumban el cliente).
  - Verificado con cliente real: sí.
- [ ] **7.4 API REST de Telemetría y Monitoreo:**
  - Jugadores conectados, salas en combate, uso de memoria y estado del servidor. Requiere C-6. Escucha solo en `localhost` por defecto.
  - Evidencia: diseño propio. Verificado con cliente real: no aplica.

---

### Pendientes detectados (sin fase asignada)

- [ ] **Mensajería y amigos (Messenger, F9):** *parcial, integrado en `src/Rakion.Buddy/`.*
  - Hecho: servidor TCP/UDP 8500, login y cifrado del Messenger, registro UDP, nick, lista de amigos al conectar (`0x1011`), estado en línea (`0x3FFF`), túnel `0x2020` → `0x2021`, consulta de estado `0x3010`, grupos (`0x3150` / `0x3152`) y `0x19 GetUserName` en el World.
  - Verificado con cliente real: login, registro UDP, nick y `0x19`. **Sin verificar:** que la lista de amigos se vea en la ventana del Messenger.
  - **Pospuesto:** agregar, aceptar y quitar amigos desde el cliente. `Buddy2.dll` v258 tiene una máscara de servicios fija a 1 que convierte esas acciones en no-ops: nunca llegan al servidor (**Observed**). Hay una idea sin parche por probar; detalle en `src/Rakion.Buddy/README.md`.
  - Pendiente: mensajes (`0x2030` / `0x2010`) y la seguridad del login (el original no prueba la contraseña).
- [ ] **Clanes:** creación, miembros, puntos y su ranking (`ClanRank` del original).

---

### Transversal (lo asigna el integrador)

- [ ] **T.1 Despliegue con Docker:** `Dockerfile` + `docker-compose.yml` con MariaDB (criterio de éxito 3). Pospuesto hasta que el servidor esté completo y estable.
- [ ] **T.2 Golden tests:** test contra bytes capturados del cliente por cada paquete marcado "verificado con cliente real: sí".

---

## Criterios de Éxito de la Migración

1. **Cliente Intacto:** El cliente de Rakion v258 debe poder conectarse, crear personajes, forjar armas, crear salas y jugar partidas completas sin crasheos y sin necesidad de loaders oscuros.
2. **Cero Dependencias de 32 bits:** Todo el servidor se ejecutará en 64 bits con .NET 10 (LTS).
3. **Despliegue con un Solo Comando:** Contenedor Docker listo (`docker-compose up`) que levante MySQL y el servidor de juego configurado (ítem T.1).
4. **Mantenibilidad:** Código estructurado bajo principios SOLID, con logs claros (Serilog) y sin parches binarios en memoria.
