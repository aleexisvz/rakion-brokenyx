# Hoja de Ruta (Roadmap): Emulador Rakion Server en C# (.NET 10 LTS)
**Proyecto:** `rakion-brokenyx`  
**Directorio Base:** `C:\Users\Alexis\Projects\C#\rakion-brokenyx`  
**Filosofía del Proyecto:** Construir un servidor limpio, modular, de alto rendimiento y de código abierto en C# que emule de forma nativa y orgánica los protocolos de Rakion v258. Cero parches binarios, cero inyecciones de memoria, cero dependencias obsoletas de 32-bit (GameGuard/MySQL 4) y soporte nativo multiplataforma (Windows y Linux/Docker).

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
│   ├── Rakion.Relay/            # Servidor UDP de Relevo P2P y Sincronización de Combate (40708/40709)
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
  - Pruebas unitarias de hasheo y 12 pruebas de integración en `tests/Rakion.Database.Tests` sobre SQLite en memoria validando transacciones de saldo, límites de slots, unicidad de personajes y CRUD de inventario.

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
  - Autenticación dinámica de `NyxLauncher.exe`: descifrado AES-128-ECB de la clave estática `0x464FA0`, verificación de desafío de contraseña con clave de sesión `SHA1` (`NyxLauncherCrypto`), y vinculación del usuario autenticado a la conexión TCP para que `0x4000` ("Start Game") emita el ticket de sesión al usuario real.
  - Validar contra la base de datos (`IUserRepository`), con actualización automática transparente a BCrypt de contraseñas heredadas (MD5 / plaintext).
  - Comprobar estado de baneo en `user_game_info` (`is_banned`, `ban_date`, `ban_reason`).
  - Responder con código de éxito (`LoginResultCode.Success`) o error de credenciales/baneo (`LoginResponsePacket`).
  - Generar un Ticket o Token de Sesión criptográfico único en memoria con TTL de 5 minutos (`ISessionTicketService` / `SessionTicketService`).
- [x] **2.4 Endpoint Opcional AutoFetch (Sustituto de `fetch.php`):**
  - Servidor HTTP ligero interno (`AutoFetchServer`) emulando el protocolo Softnyx AutoFetch para `NyxLauncher.exe` (`fetch.php`), sirviendo URLs de avisos, parches y versión objetivo sin depender de servidores web externos.

---

### Fase 3: World Server (Gestión de Personajes, Canales y Lobby)
> **Objetivo:** Lograr que el cliente entre al juego en el puerto 40708, cargue sus personajes, permita crear/borrar y acceda al canal de Lobby.
> **Estado:** Items 3.1 a 3.5 Completados y verificados en juego con cliente retail v258 (85%). En desarrollo: Item 3.6 (Canales y Lobby).

- [x] **3.1 Servidor TCP y Dual UDP en Puertos 40708/40709:** Listener principal TCP de sesiones de juego y listeners duales UDP para responder a sondeos de NAT traversal / keep-alive del cliente.
- [x] **3.2 Handshake de Sesión:** El cliente presenta su Token de Sesión obtenido del Broker y el WorldServer asocia el socket con la cuenta `CUser`.
- [x] **3.3 Bypass Orgánico de GameGuard:**
  - Cuando el cliente envíe su paquete de verificación (`SendGameGuard`), el servidor responde con la confirmación de validación correcta de forma transparente. Esto elimina para siempre la necesidad de DLLs `ggauth*.dll` o parches en el cliente.
- [x] **3.4 Listado y Selección de Personajes (`SendCharacterList` y `SendCharacterSelect`):**
  - Consultar personajes de la cuenta en `characterinfo` y serializar sus datos (Clase, Nivel, Modelo 3D, Estadísticas calculadas, Equipamiento visual actual).
  - Al seleccionar un personaje, colocarlo en estado activo de sesión.
- [x] **3.5 Creación y Borrado de Personajes:**
  - Implementar `SendCharacterCreate`: verificar que el nombre no esté en uso, crear fila con clase seleccionada (0 a 4) y otorgar el equipamiento básico inicial.
  - Implementar `SendCharacterDelete`: verificar la clave de borrado (`deletekey`) y retirar el personaje.
- [ ] **3.6 Sistema de Canales de Lobby:**
  - Implementar canales: Canal de Principiantes (con límite de nivel), Canal Libre, Canal de Clanes.
  - Procesar entrada (`SendChannelEnter`) y salida (`SendChannelExit`).
  - Chat general del canal (`SendChannelChat`) con difusión a todos los sockets presentes en el canal.
  - Mensajes privados / susurros (`SendCharacterWhisper`).

---

### Fase 4: Economía, Inventario, Tienda y Herrería
> **Objetivo:** Permitir a los jugadores gestionar sus objetos, comprar en la tienda y mejorar equipamiento en la herrería.

- [ ] **4.1 Carga y Envío de Inventario (`SendInventoryEnter`):**
  - Enviar al cliente el mapa de ranuras ocupadas (armas, cascos, pecheras, accesorios, criaturas invocables, pociones) con sus niveles de forja y duraciones.
- [ ] **4.2 Tienda de Objetos (`SendInventoryBuy`):**
  - Validar precio en oro o cash en el catálogo `ItemInfo`.
  - Descontar saldo de forma atómica y asignar el item en `UserItemInfo`.
- [ ] **4.3 Venta y Movimiento de Items (`SendInventorySell` y `SendInventoryMove`):**
  - Permitir reorganizar objetos entre bolsas y vender items por oro.
- [ ] **4.4 Asignación de Puntos de Nivel (`SendInventoryAllocationPoint`):**
  - Permitir repartir puntos a las estadísticas primarias (`hit1`–`hit4`, `chit`, `hp`, `ap`, velocidad, CP) y recalcular atributos totales.
- [ ] **4.5 El Sistema de Herrería y Refinado (`SendEncahntReinforce`):**
  - Lógica matemática de refinamiento con gemas:
    - Nivel 0 a +3: 100% éxito garantizado (protección contra destrucción).
    - Nivel +4 a +15: Probabilidad configurable de éxito, degradación (-1) o rotura.
  - Sincronización instantánea de las nuevas estadísticas del item.

---

### Fase 5: Sistema de Salas (Matchmaking y Modos de Juego)
> **Objetivo:** Permitir la creación de salas de espera, cambio de equipos, selección de mapas y cuenta regresiva de inicio.

- [ ] **5.1 Lista de Salas (`SendFieldList`):** Retornar salas con paginación, mostrando nombre, anfitrión, modo, mapa y cantidad de jugadores actuales.
- [ ] **5.2 Creación de Sala (`SendFieldCreate`):**
  - Configurar parámetros: Título de la sala, contraseña opcional, modo de juego (Deathmatch individual, por equipos, Golem Wars, Leader Wars, Boss Raid, Escenarios PVE), mapa y límite de niveles.
  - Asignar al creador como Host / Master de la sala.
- [ ] **5.3 Sala de Espera (Lobby de Sala):**
  - Asignación de slots (hasta 16 jugadores en 8v8).
  - Cambio de equipo (Equipo Rojo / Equipo Azul) con `SendFieldChangeTeam`.
  - Estado Preparado (`SendFieldReady`): Validar que todos los jugadores tengan el estado listo antes de que el Host pueda iniciar.
  - Chat exclusivo de la sala de espera (`SendFieldChat`).
  - Sistema de expulsión de la sala (`SendFieldKick` y votaciones `SendFieldGameVote`).
- [ ] **5.4 Inicio de Partida (`SendFieldGameStart`):**
  - Notificar a todos los clientes para que comiencen la carga del mapa y los modelos 3D (`LevelsSV.xfs` y `ModelsSV.xfs`).

---

### Fase 6: Combate en Tiempo Real y Servidor UDP Relay
> **Objetivo:** Permitir que los jugadores jueguen la partida de forma fluida, intercambien golpes y el servidor registre muertes, rondas y recompensas.

- [ ] **6.1 Servidor UDP en Puertos 40708 y 40709:**
  - Sockets UDP de alta concurrencia para paquetes ligeros en tiempo real.
  - Registro y mapeo de endpoints públicos de cada cliente (`SendSuccessUDP`, `SendFieldSlotUDP`) para resolver problemas de NAT estricto.
- [ ] **6.2 Mecanismo de Relevo (Relay / Tunneling):**
  - Implementar `SendFieldGameTunnelingOne` y `SendFieldGameTunnelingAll` para reenviar los paquetes de posición, ataques, invocaciones de criaturas y animaciones entre los jugadores de la misma sala cuando la conexión directa P2P no sea posible.
- [ ] **6.3 Arbitraje de Eventos Críticos:**
  - Muerte de jugadores (`SendFieldGameDiePlayer`): Registrar qué usuario asesinó a quién, el modo y la causa.
  - Sincronización de entidades maestras: vida de Golems en Golem Wars (`SendFieldGameMasterGolem`) y jefes en Boss Raid (`SendFieldGameMasterBossHP`).
- [ ] **6.4 Fin de Ronda y Partida (`SendFieldGameRoundEnd` y `SendFieldGameEnd`):**
  - Determinar equipo ganador según las condiciones de victoria de cada modo.
  - Cálculo de Puntuaciones (`SendFieldGamePoint`):
    - Fórmula limpia de experiencia (EXP) y oro ganados según bajas, daño infligido y bonificaciones de victoria/PowerUser.
  - Persistencia segura de estadísticas en la base de datos y envío de la pantalla final de recompensas.

---

### Fase 7: Servicios de Mantenimiento, Rankings y Herramientas GM
> **Objetivo:** Dotar al ecosistema de herramientas de administración en vivo, sistema de rangos y detección de tramposos.

- [ ] **7.1 Motor de Rankings Atómicos (Reemplazo de `RankUpdate.exe`):**
  - Tarea programada en C# (BackgroundService / Quartz.NET) que ejecute periódicamente el recálculo de clasificaciones generales y por clase con la técnica de doble buffer.
  - Asignación automática de los Grados/Medallas del 26 al 15 (y grados superiores de ranking).
- [ ] **7.2 Módulo Antifraude y Detector de Abusers:**
  - Detección algorítmica de partidas arregladas (*win-trading* o bots) que superen umbrales anómalos de victorias, oro o experiencia en períodos de 24 horas.
- [ ] **7.3 Comandos de Administrador en el Chat (In-Game GM Commands):**
  - `/notice <mensaje>`: Difusión de anuncios del servidor a todos los canales.
  - `/kick <jugador>`, `/ban <jugador> <días>`: Moderación en vivo.
  - `/givegold <jugador> <monto>`, `/givecash <jugador> <monto>`, `/giveitem <jugador> <itemId>`: Asignación de recursos y soporte a eventos.
- [ ] **7.4 API REST de Telemetría y Monitoreo:**
  - Dashboard web moderno para observar en tiempo real: jugadores conectados, salas en combate, uso de memoria y estado del servidor.

---

## Criterios de Éxito de la Migración

1. **Cliente Intacto:** El cliente de Rakion v258 debe poder conectarse, crear personajes, forjar armas, crear salas y jugar partidas completas sin crasheos y sin necesidad de loaders oscuros.
2. **Cero Dependencias de 32 bits:** Todo el servidor se ejecutará en 64 bits con .NET 8/9.
3. **Despliegue con un Solo Comando:** Contenedor Docker listo (`docker-compose up`) que levante MySQL y el servidor de juego configurado.
4. **Mantenibilidad:** Código estructurado bajo principios SOLID, con logs claros (Serilog) y sin parches binarios en memoria.

