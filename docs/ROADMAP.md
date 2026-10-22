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
| **Compartido** | Un único dueño a la vez | `src/Rakion.Core/`, `src/Rakion.App/`, `config/appsettings.json`, `RakionBrokenyx.sln`, `Directory.Build.props` | Contratos |
| **Integrador** | Mantenedor | Checkboxes de este archivo, `README.md` raíz, `AGENTS.md` | — |

### Contratos Compartidos

Cada contrato se entrega en un PR pequeño que se mergea antes de que el otro carril lo consuma.

| ID | Contrato | Dueño | Consumidor | Bloquea |
| :--- | :--- | :--- | :--- | :--- |
| **C-0** | Separar `RakionOpcode` en `ClientOpcode` (C→S) y `ServerOpcode` (S→C) y eliminar alias con valores repetidos, sin cambiar bytes en el cable. | B | A, B | 4.1, 5.x |
| **C-1** | `IWorldOpcodeModule`: registro tipado `opcode → handler` en World; `WorldPacketHandler` delega en él los opcodes que no maneja. | B | A (`EconomyModule`) | 4.1 |
| **C-2** | `IWorldSessionView` (usuario, personaje, canal, sala, en partida) + `ISessionSender` (enviar a una sesión). | B | A | 4.1, 7.3, 7.4 |
| **C-3** | Paquetes de inventario, tienda, herrería y estadísticas en `Rakion.Core/Packets/`. | A | B | 4.x |
| **C-4** | `IMatchResultService` en Database: persistir EXP, oro, victorias/derrotas y `logdieplayer` en una transacción. | A | B | 6.4 |
| **C-5** | `IChatCommandInterceptor`: el chat de canal y de sala lo consulta antes de difundir. | B | A | 7.3 |
| **C-6** | `IWorldTelemetry`: sesiones, salas y partidas activas. | B | A | 7.4 |

### Reglas de Integración

1. Cada carril actualiza el README de sus módulos. Los checkboxes de este archivo solo los marca el integrador al mergear.
2. Cada PR declara qué paquetes y contratos de `Rakion.Core` usa o modifica.
3. Cada paquete tiene un test de round-trip (serializar → bytes → parsear). Quien lo añade es su dueño; el otro carril lo consume sin reescribirlo.
4. Cada ítem lleva su etiqueta de evidencia y la marca "verificado con cliente real: sí/no".
5. Si un carril necesita un opcode o layout del otro, no lo implementa: lo pide al mantenedor y se coordina.
6. Antes de tocar una ruta compartida, el carril lo anuncia al mantenedor; el otro carril no la toca hasta que ese PR se mergee.

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
│   └── Rakion.App/              # Punto de entrada unificado / Daemon CLI para ejecutar todos los servicios
├── tests/
│   ├── Rakion.Core.Tests/       # Pruebas unitarias de serialización de paquetes, opcodes y AES-128
│   ├── Rakion.Network.Tests/    # Pruebas de integración de sockets y concurrencia TCP
│   ├── Rakion.Database.Tests/   # Pruebas de persistencia, concurrencia de balance y repositorios SQLite
│   ├── Rakion.Broker.Tests/     # Pruebas de autenticación Broker, tickets de sesión y AutoFetch HTTP
│   └── Rakion.World.Tests/      # Pruebas de ciclo de vida de WorldServer, login y UDP hole punching
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
> **Estado:** Completada (100%). Ítems 3.1 a 3.6 terminados con pruebas automatizadas. Verificado con cliente real: 3.1–3.5 sí; 3.6 pendiente de confirmar por el mantenedor.

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

- [ ] **4.0 Prerrequisitos:** contratos C-0, C-1 y C-2 mergeados en `main`.
- [ ] **4.1 Carga y Envío de Inventario (`SendInventoryEnter`):**
  - Enviar al cliente el mapa de ranuras ocupadas (armas, cascos, pecheras, accesorios, criaturas invocables, pociones) con sus niveles de forja y duraciones.
  - Evidencia: nombre del método **Observed** (símbolo `IScavengerWorldNet`); opcode y layout de la respuesta **Unknown** (requiere observación del cliente). Lectura de inventario en BD: ya existe (`IInventoryRepository.GetUserItemsAsync`, 1.3).
  - Verificado con cliente real: no.
- [ ] **4.2 Tienda de Objetos (`SendInventoryBuy(uint16 itemId, uint8 type, uint8 period, uint16 price)`):**
  - Validar precio en oro o cash contra el catálogo `ItemInfo` en servidor (el `price` enviado por el cliente nunca se usa como verdad).
  - Descontar saldo de forma atómica y asignar el item en `UserItemInfo` en la misma transacción, con log.
  - Evidencia: firma **Derived** (de la firma del símbolo); semántica de `type`/`period` **Inferred**; opcode **Unknown**.
  - Verificado con cliente real: no.
- [ ] **4.3 Venta y Movimiento de Items (`SendInventorySell(uint8 slot)` y `SendInventoryMove(uint8 src, uint8 dst, ...)`):**
  - Permitir reorganizar objetos entre bolsas y vender items por oro.
  - Evidencia: `Sell` **Derived**; parámetros extra de `Move` **Unknown**; `CUser::ulSellGold` **Observed** (símbolo), fórmula del precio de venta **Unknown** (configurable).
  - Verificado con cliente real: no.
- [ ] **4.4 Asignación y Reseteo de Puntos (`SendInventoryAllocationPoint(uint8 statIndex)`, `SendCharacterStateClear(uint8, uint16 cost)`):**
  - Permitir repartir puntos a las estadísticas primarias (`hit1`–`hit4`, `chit`, `hp`, `ap`, velocidad, CP) y recalcular atributos totales.
  - Evidencia: opcode de `StateClear` `0x001B` **Observed**; mapeo `statIndex → estadística` **Inferred**; opcode de `AllocationPoint` **Unknown**.
  - Verificado con cliente real: no.
- [ ] **4.5 El Sistema de Herrería y Refinado (`SendEncahntReinforce`):**
  - Cadena `"Not Destroy below lv3 Item!!"` en `CUser::CheckEnchantReinforce`: **Observed**. Prueba solo que los items por debajo de lv3 no se destruyen.
  - "0 a +3 con 100 % de éxito": **Inferred** (no probado). Nivel máximo (+15) y probabilidades de éxito/degradación/rotura: **Unknown** → configurables en `appsettings.json`.
  - Sincronización instantánea de las nuevas estadísticas del item.
  - Verificado con cliente real: no.
- [ ] **4.6 Regalos (`SendPresentPeek` / `SendPresentAccept`)** *(opcional)*:
  - Evidencia: nombres **Observed**; layout **Unknown**.
  - Verificado con cliente real: no.

---

### Fase 5: Sistema de Salas (Matchmaking y Modos de Juego)
> **Objetivo:** Permitir la creación de salas de espera, cambio de equipos, selección de mapas y cuenta regresiva de inicio.
> **Carril:** B. **Código:** `src/Rakion.World/` (excepto `Features/Economy/`).

- [ ] **5.1 Lista de Salas (`SendFieldList`, `0x0036`):** Retornar salas con paginación, mostrando nombre, anfitrión, modo, mapa y cantidad de jugadores actuales.
  - Evidencia: opcode **Observed** (respuesta vacía ya enviada en 3.6); layout de cada entrada **Unknown**. Verificado con cliente real: no.
- [ ] **5.2 Creación de Sala (`SendFieldCreate`, `0x0053`):**
  - Configurar parámetros: Título de la sala, contraseña opcional, modo de juego (Deathmatch individual, por equipos, Golem Wars, Leader Wars, Boss Raid, Escenarios PVE), mapa y límite de niveles.
  - Asignar al creador como Host / Master de la sala.
  - Evidencia: opcode **Observed**; valores numéricos de `GameMode` **Inferred**. Verificado con cliente real: no.
- [ ] **5.3 Sala de Espera (Lobby de Sala):**
  - Asignación de slots (capacidad máxima **Unknown**; "16 jugadores en 8v8" es **Inferred**).
  - Cambio de equipo (Equipo Rojo / Equipo Azul) con `SendFieldChangeTeam` (opcode **Unknown**).
  - Estado Preparado (`SendFieldReady`, `0x0061` **Observed**): validar que todos estén listos antes de que el Host pueda iniciar.
  - Chat exclusivo de la sala de espera (`SendFieldChat`, `0x007F` **Observed**), entrada rápida (`0x004F` **Observed**) y salida (`0x0051` **Observed**).
  - Sistema de expulsión de la sala (`SendFieldKick` y votaciones `SendFieldGameVote`; opcodes **Unknown**).
  - Verificado con cliente real: no.
- [ ] **5.4 Inicio de Partida (`SendFieldGameStart`):**
  - Notificar a todos los clientes para que comiencen la carga del mapa y los modelos 3D (`LevelsSV.xfs` y `ModelsSV.xfs`).
  - Evidencia: nombre **Observed**; opcode **Unknown**. Verificado con cliente real: no.

---

### Fase 6: Combate en Tiempo Real y Relay UDP
> **Objetivo:** Permitir que los jugadores jueguen la partida de forma fluida, intercambien golpes y el servidor registre muertes, rondas y recompensas.
> **Carril:** B. **Código:** `src/Rakion.Relay/` (librería sin sockets), `src/Rakion.World/` (listeners UDP ya existentes desde 3.1). 6.4 consume el contrato C-4 del carril A.

- [ ] **6.1 Registro de Endpoints UDP por Slot de Sala (`SendFieldSlotUDP`):**
  - El bind de los puertos UDP 40708/40709 y el sondeo `0x0201`/`0x0202`/`0x000E` ya están hechos en 3.1 (`Rakion.World`).
  - Asociar a cada slot de sala el endpoint público observado de su cliente para resolver NAT estricto.
  - Evidencia: nombre **Observed**; layout **Unknown**. Verificado con cliente real: no.
- [ ] **6.2 Mecanismo de Relevo (Relay / Tunneling, `0x0401`):**
  - Crear `Rakion.Relay` como librería pura (`RelayRouter`): enrutamiento `SendFieldGameTunnelingOne` / `SendFieldGameTunnelingAll` entre los miembros de la misma sala, con limitación de tasa. `WorldUdpPacketHandler` le despacha `0x0401`.
  - Evidencia: opcode **Observed**; layout **Unknown**. Verificado con cliente real: no.
- [ ] **6.3 Arbitraje de Eventos Críticos:**
  - Muerte de jugadores (`SendFieldGameDiePlayer(uint8 victimSlot, uint8 cause)`): registrar quién mató a quién, el modo y la causa.
  - Sincronización de entidades maestras: Golems (`SendFieldGameMasterGolem`) y jefes (`SendFieldGameMasterBossHP`).
  - Evidencia: nombres **Observed**; parámetros **Derived** de las firmas; opcodes **Unknown**. Verificado con cliente real: no.
- [ ] **6.4 Fin de Ronda y Partida (`SendFieldGameRoundEnd`, `SendFieldGameEnd`, `SendFieldGamePoint`):**
  - Determinar equipo ganador según las condiciones de victoria de cada modo.
  - Fórmula de EXP y oro: **Unknown** en el original → diseño propio configurable.
  - Persistencia mediante `IMatchResultService` (C-4, carril A) y envío de la pantalla de recompensas.
  - Verificado con cliente real: no.

---

### Fase 7: Servicios de Mantenimiento, Rankings y Herramientas GM
> **Objetivo:** Dotar al ecosistema de herramientas de administración en vivo, sistema de rangos y detección de tramposos.
> **Carril:** A. **Código:** `src/Rakion.Maintenance/` (nuevo, hospedado por `Rakion.App`), `src/Rakion.Database/`.

- [ ] **7.1 Motor de Rankings Atómicos (Reemplazo de `RankUpdate.exe`):**
  - `BackgroundService` que recalcule periódicamente las clasificaciones generales y por clase con doble buffer (`TotalRank` → `TotalRankP`, **Observed** en cadenas SQL de `RankUpdate.exe`).
  - Tabla de grados 26 → 15 por EXP: **Inferred** hasta contrastarla con datos reales; grados 14 → 1: **Unknown**.
  - Verificado con cliente real: no.
- [ ] **7.2 Módulo Antifraude y Detector de Abusers:**
  - Umbrales `CheckWinSum=200`, `CheckExpSum=6000`, `CheckGoldSum=12000` y consulta de agregación de 24 h: **Observed** (`Config.ini` y SQL de `RankUpdate.exe`).
  - Solo marca para revisión de un GM; no banea automáticamente.
  - Verificado con cliente real: no aplica (proceso batch).
- [ ] **7.3 Comandos de Administrador en el Chat (In-Game GM Commands):**
  - `/notice <mensaje>`, `/kick <jugador>`, `/ban <jugador> <días>`, `/givegold`, `/givecash`, `/giveitem`.
  - Evidencia: sintaxis de **diseño propio** (sin evidencia de comandos en el retail). Requiere C-5. Toda asignación de oro/cash/items en transacción y con log.
  - Verificado con cliente real: no.
- [ ] **7.4 API REST de Telemetría y Monitoreo:**
  - Jugadores conectados, salas en combate, uso de memoria y estado del servidor. Requiere C-6. Escucha solo en `localhost` por defecto.
  - Evidencia: diseño propio. Verificado con cliente real: no aplica.

---

### Transversal (lo asigna el integrador)

- [ ] **T.1 Despliegue con Docker:** `Dockerfile` + `docker-compose.yml` con MariaDB (criterio de éxito 3).
- [ ] **T.2 Golden tests:** test contra bytes capturados del cliente por cada paquete marcado "verificado con cliente real: sí".

---

## Criterios de Éxito de la Migración

1. **Cliente Intacto:** El cliente de Rakion v258 debe poder conectarse, crear personajes, forjar armas, crear salas y jugar partidas completas sin crasheos y sin necesidad de loaders oscuros.
2. **Cero Dependencias de 32 bits:** Todo el servidor se ejecutará en 64 bits con .NET 10 (LTS).
3. **Despliegue con un Solo Comando:** Contenedor Docker listo (`docker-compose up`) que levante MySQL y el servidor de juego configurado (ítem T.1).
4. **Mantenibilidad:** Código estructurado bajo principios SOLID, con logs claros (Serilog) y sin parches binarios en memoria.
