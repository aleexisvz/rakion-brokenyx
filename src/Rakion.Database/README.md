# Rakion.Database

`Rakion.Database` proporciona la capa de persistencia relacional limpia, modelado de entidades y seguridad de credenciales para el emulador `rakion-brokenyx`. Diseñado para eliminar los fallos arquitectónicos y dependencias de 32-bit del servidor original de 2005 (MySQL 4/MyISAM, tipos `datetime` inválidos y contraseñas en texto plano o MD5).

## Responsabilidades y Alcance

- **Compatibilidad Moderna (MySQL 8.0+ / MariaDB 10.5+):** Utiliza el motor de almacenamiento **InnoDB** con claves foráneas reales, índices optimizados y soporte completo para transacciones ACID.
- **Prevención de Explotación y Duplicación de Moneda:** Las operaciones de débito de oro y cash (`TryDeductGoldAsync`, `TryDeductCashAsync`) utilizan actualizaciones condicionales a nivel de base de datos (`WHERE gold >= @Amount`), garantizando protección matemática contra condiciones de carrera (*race conditions*) y duplicación de ítems (*duping*).
- **Seguridad y Migración Transparente de Contraseñas:** Soporte nativo para **BCrypt** de última generación, con detección y migración automática en caliente para cuentas heredadas con hashes MD5 o contraseñas en texto plano.
- **Micro-ORM de Alto Rendimiento:** Implementado con **Dapper** y **MySqlConnector** para máxima velocidad asíncrona y mínimo consumo de memoria.

## Contenido del Módulo

### 1. Entidades de Dominio (`Rakion.Database.Entities`)
- **`UserEntity`:** Credenciales de cuenta, correo electrónico, país y nivel de autoridad GM.
- **`UserGameInfoEntity`:** Balance de oro, tutorial, estado de baneo, slots de personajes desbloqueados (por defecto 3 en v258), bolsas de inventario y estado PowerUser.
- **`CashEntity`:** Saldo de dinero real (Cash).
- **`CharacterEntity`:** Datos del personaje, clase (`CharacterClass`), nivel, experiencia, puntos sin repartir, atributos de combate (`Hit1`–`Hit4`, `Chit`, `Hp`, `Ap`, `AttackSpeed`, `Speed`, `MaxCp`) y clave de borrado (`DeleteKey`).
- **`ItemEntity`:** Catálogo maestro de armas, armaduras, accesorios y células de invocación con precios y requisitos de nivel.
- **`UserItemEntity`:** Instancias de objetos pertenecientes a los jugadores con nivel de forja/refinamiento, experiencia acumulada de criaturas y duración de renta.
- **`ClanEntity`** y **`BuddyEntity`:** Datos de gremios y sistema social de amigos.
- **`ClassLevelEntity`** y **`CreatureLevelEntity`:** Tablas de niveles del personaje (`class_level_info`, del `DataSetup.xfs` del cliente) y de las criaturas (`creature_level_info`, `NPCInfo` del dump).
- **`RankingCandidate`** y **`RankingEntry`:** Personajes que entran al ranking y sus nuevos puestos.

### 2. Seguridad y Criptografía (`Rakion.Database.Security`)
- **`IPasswordHasher` / `PasswordHasher`:**
  - Hasheo moderno con BCrypt (factor de costo 11).
  - Reconocimiento de hashes MD5 (cadenas hexadecimales de 32 caracteres) y contraseñas legadas.
  - Bandera `out bool needsRehash` para que el servidor actualice de forma transparente la base de datos al nuevo formato seguro en el primer inicio de sesión exitoso.

### 3. Repositorios de Persistencia (`Rakion.Database.Repositories`)
- **`IUserRepository` / `UserRepository`:** Búsqueda por nombre de usuario e ID, creación atómica de cuentas y actualización de sellos de conexión.
- **`ICharacterRepository` / `CharacterRepository`:** Validación de límites de slots, unicidad de nombres, creación y borrado seguro con clave.
- **`IAccountBalanceRepository` / `AccountBalanceRepository`:** Gestión segura y transaccional de oro y cash.
- **`IInventoryRepository` / `InventoryRepository`:** Catálogo, box y equipo, compra, venta, movimiento y refinado de objetos (cada cambio en una transacción).
- **`IMatchResultRepository` / `MatchResultRepository`:** Recompensas de ronda y de stage (EXP, oro, nivel, victorias/derrotas/empates, EXP de criaturas, rangos y drops de stage) y tablas de niveles (contrato C-4).
- **`IAccountUpgradeRepository` / `AccountUpgradeRepository`:** Reseteo de puntos, bolsas y slots de poción.
- **`IAdminRepository` / `AdminRepository`:** Baneos y entrega de ítems de los comandos GM.
- **`IBuddyRepository` / `BuddyRepository`:** Amigos del Messenger (tabla `buddies`, dos filas por amistad), grupos (`buddy_groups`, se crea al primer uso, máximo 50) y nombre visible.
- **`IRankingRepository` / `RankingRepository`:** Población del ranking, puestos y tablas `ranking_total` / `ranking_class`.

### 3b. Importadores (`Rakion.Database.Import`)
- Catálogo de ítems, tabla de criaturas (`NPCInfo`) desde el dump del servidor original y tabla de niveles desde el `DataSetup.xfs` del cliente. Los usa `Rakion.App` con `--import-items` y `--import-levels`.

### 4. Esquema SQL (`src/Rakion.Database/Migrations/schema.sql`)
- Script DDL completo con motor InnoDB, juego de caracteres `utf8mb4_unicode_ci` y restricciones de integridad referencial.
