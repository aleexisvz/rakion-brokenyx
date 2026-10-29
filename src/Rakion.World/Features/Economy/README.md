# Rakion.World / Features / Economy

Economía del juego (fase 4 del roadmap): inventario, tienda, venta, reparto de puntos y
herrería. **Carril A.** El resto de `Rakion.World` pertenece al carril B.

## Cómo se engancha a World

- Cada módulo implementa `IWorldOpcodeModule` (contrato C-1) y se registra en DI en
  `Rakion.App` (`AddSingleton<IWorldOpcodeModule, ...>()`). No se edita `WorldPacketHandler`.
- El estado de la sesión se lee solo a través de `IWorldSessionView` (contrato C-2). Las
  respuestas se envían con `ISessionSender`.
- Los paquetes viven en `Rakion.Core/Packets/InventoryPackets.cs` (contrato C-3) y cada uno
  tiene su test de round-trip.

## Estado

| Ítem | Opcodes (cliente → servidor) | Estado | Verificado con cliente real |
| :--- | :--- | :--- | :--- |
| 4.1 Carga de inventario | `InventoryEnter` `0x002C`, `InventoryLeave` `0x002D` (**Observed**) | Responde `0x002C` con `[u8 Result][u32 ServerTimeMinutes]` (**Observed**). El Box de la cuenta (120 slots) viaja en la cola de `WorldLoginPart2` (`0x000D`) y lo llena `AccountInventoryProvider`. Los 19 slots de equipo de cada personaje viajan en sus registros de `0x000C`/`0x000D`. | `0x002C`: sí. Box: pendiente de probar |
| 4.3 Mover ítems | `InventoryMove` `0x0031` (**Observed**) | Box ↔ Box, Box ↔ equipo y equipo ↔ equipo del personaje activo: intercambio atómico en BD y respuesta `0x0031` con el estado nuevo de ambos slots. Falta validar qué ítem cabe en cada slot de equipo (reglas **Unknown**).  | Box: sí. Equipo: pendiente de probar |
| 4.3 Vender ítems | `InventorySell` `0x002F` (**Observed**) | Venta desde el Box por oro con la fórmula del original; borrado del ítem y abono en una transacción. Rechazadas por ahora: criaturas (8000–8999). | pendiente de probar |
| 4.2 Tienda | `InventoryBuy` `0x002E` (**Observed**) | Compra con oro o cash: validación como el original (tienda 1/2/3, saldo), slot libre más alto dentro de las bolsas, cobro e inserción en una transacción y respuesta con saldos e ítem. Rechazados por ahora: cupones y paquetes (tipo 10). Ítems PowerUser: solo con PowerUser activo, como el original. | pendiente de probar |
| 4.4 Reparto de puntos | `InventoryAllocationPoint` `0x0033` (**Observed**) | Gasta un punto de nivel (o de PowerUser si no quedan) en una de las 10 estadísticas, en una transacción; tope 50 por estadística (**Inferred**). Reseteo (`0x001B`) pendiente. | pendiente de probar |
| 4.2 PU Card | `InventoryBuyPowerUser` `0x0034` (**Observed**) | Compra nueva (8000 cash, caduca ahora + 30 días) o renovación (6000 cash, +30 días), +5 puntos PowerUser, tope de 180 días por delante; una transacción. Cupones rechazados. | pendiente de probar |
| 4.5 Herrería | `EnchantReinforce` `0x0074` (**Observed**) | Refinado de un ítem de equipo del Box con una piedra (13001–13005): valida como el original, sortea el resultado con probabilidades configurables (**Inferred**), consume la piedra y ajusta el nivel en una transacción. Sin rotura (como el original). Materiales 14001–14003 rechazados por ahora. | pendiente de probar |
| 4.6 | ver `ClientOpcode` (`0x002E`–`0x0035`, `0x006B`–`0x0074`, **Observed**) | Pendiente | no |

## Evidencia y decisiones

- Opcodes: leídos del inmediato con el que cada `IScavengerWorldNet::Send*` de `engine.dll`
  (v258) escribe el opcode. El método se validó con `SendAlive` (`0x000F`) y
  `SendCharacterStateClear` (`0x001B`), que ya estaban confirmados.
- Respuesta a `0x002C` (observada en el desensamblado, sin copiar código):
  - `RakionWorldServ.exe` 0x427A9D envía 7 bytes (opcode, `u8` resultado, `u32` valor) y
    `engine.dll` 0x36193650 lee exactamente `u8` + `u32`.
  - `DBCommandInventoryEnter` comprueba la pertenencia con
    `SELECT userid=%u FROM characterinfo WHERE id=%u` y obtiene el valor con
    `SELECT (to_days(now())*24+hour(now()))*60+minute(now())`. Ese valor es la hora del
    servidor en minutos; lo reproduce `LegacyGameClock`, con hora local como `now()` de MySQL.
  - Resultado `0` = éxito, `1` = error de BD. Si el personaje no es de la cuenta, el original
    no responde y emite un error interno `0xB8` (semántica **Unknown**). Aquí la selección de
    personaje ya garantiza la pertenencia.
  - Uso del valor en el cliente (tiempo restante de ítems alquilados): **Inferred**.
- Box de la cuenta (`AccountInventoryProvider`, `AccountBoxBlock`):
  - El parser del cliente (`engine.dll` 0x36196B41..0x36196C71) lee: `u8 N` + N × `u16` (caducados),
    un `u8` con el número de bolsas, 120 × `u16` itemId, 120 × `u8` level, 120 × `u32`, 120 × `u32` y un `u32`
    final (**Unknown**). **Observed.**
  - El `u8` es el número de bolsas (`user_game_info.bag_count`; 30 slots por bolsa, **Observed** en la búsqueda de slot libre de la compra): `rakion.bin`
    0x47E75A lo guarda en `listener+0x80`, el mismo campo que actualiza la respuesta a BuyBag
    (0x475283). Offsets **Observed**, significado **Derived**. Con 0 el cliente no dibuja ningún slot.
  - Que los dos arrays de `u32` sean exp y limittime es **Derived** del orden de copia del servidor
    original (`RakionWorldServ.exe` 0x412065..0x4120E6).
  - Reglas del servidor original (`DBCommandCharacterList`), aplicadas igual: las filas con
    `characterid = 0` van al slot indicado por `slot`; los alquileres con `limittime` ya pasado se
    listan como caducados; las filas de personaje no entran en el Box.
  - Diferencias propias: se ignoran (con aviso en el log) los slots fuera de 0..119 o repetidos y los
    itemId que no caben en `u16`.
  - `user_items.limit_time_minutes` debe guardarse en la unidad de `LegacyGameClock` (0 = permanente).
- Salida del inventario: el cliente espera `0x002D` con `[u8 Result]` (`engine.dll` 0x36193680,
  **Observed**); sin respuesta se queda en "leaving inventory". Se responde 0.
- Mover ítems (`0x0031`):
  - Petición `[u8 ubicación origen][u8 slot origen][u8 ubicación destino][u8 slot destino]`; ubicación 0 = Box
    (slots 0..119), otro valor = equipo (0..18). **Observed** en `RakionWorldServ.exe` 0x4218EF.
  - Respuesta de 19 bytes: `u8` resultado + dos bloques `(u8 ubicación, u8 slot, u16 itemId, u8 level, u32 exp)`
    con el contenido nuevo de origen y destino. Tamaños **Observed** (`engine.dll` 0x36193810); que el `u32`
    sea exp es **Derived**.
  - Resultados del original (0x40CF10): 0 = movido, 3 = origen vacío, 4 = no se puede colocar ahí
    (significados **Derived**). Fuera de rango, el original desconecta (0x3E); aquí se responde 4.
- Equipo de cada personaje: 19 slots por registro de personaje (`CharacterEquipment`): 19 × `u16` itemId, 19 × `u8`
  level, 19 × `u32` exp, 19 × `u32` limittime. Tamaños **Observed** (`engine.dll` 0x36196450..0x361964F0); orden de
  exp/limittime **Derived** de la copia del original (`RakionWorldServ.exe` 0x411E78..0x411F26). Las filas con
  `character_id` = id del personaje van a su slot `slot_index`.
- Al mover, la ubicación 1 (equipo) se refiere al personaje seleccionado. El original comprueba si el ítem puede ir a
  ese slot (0x40BC10, resultado 4); esas reglas aún no se han observado, así que por ahora manda la validación del
  propio cliente.
- Tienda (`0x002E`):
  - Catálogo: `Rakion.App --import-items <dump.sql>` carga las filas `iteminfo` de tu copia local del dump original
    en `item_templates` (el dump nunca entra en el repo).
  - Petición `[u16 itemId][u8 pagar con oro][u8 cupón]` y `[u16 slot del cupón]` solo si hay cupón. **Observed**
    (`SendInventoryBuy`); significados **Derived** del original (`RakionWorldServ.exe` 0x4212BE, 0x40CB10).
  - Respuesta: `u8` resultado, `u32` oro, `u32` cash, `u8` N y, si hay éxito, N slots, N itemId, N niveles; después
    `u8` M + M × `u32` (**Unknown**, se envía vacío). **Observed** en `engine.dll` 0x36195640; `rakion.bin`
    0x4774E0 guarda oro y cash como saldos nuevos.
  - Reglas del original (0x40A6C0): tienda 1 = solo oro, 2 = solo cash, 3 = ambos; el saldo debe cubrir el precio.
    Resultados: 0 comprado, 3 no comprable o saldo insuficiente, 4 Box lleno, 0x14–0x16 cupones.
  - Slot destino: el más alto libre en `0 .. bolsas × 30 − 1` (0x40CCBE). Nivel inicial 1 (default de
    `useriteminfo.level`).
- Venta (`0x002F`):
  - Petición `[u8 slot del Box]`; respuesta `[u8 resultado][u32 oro][u32 cash][u8 slot]`. **Observed**
    (`SendInventorySell`, `engine.dll` 0x361936A0); `rakion.bin` 0x478A70 actualiza saldos y vacía el slot.
  - Precio del original (0x40CD70 / 0x40A810): tienda 1 → 40 % del precio en oro; tienda 2 → 150 % del precio en
    cash, pagado en oro; resto → 0; ids 12000–12999 → 0. Constantes 0.4 y 1.5 **Observed**; se trunca.
  - Criaturas (8000–8999): el original usa una tabla de precios por nivel aún **Unknown**; se rechazan (resultado 3).
- Reparto de puntos (`0x0033`):
  - Petición `[u8 estadística]` (0..9: Hit1–Hit4, Chit, HP, AP, vel. de ataque, velocidad, CP máx.). Respuesta
    `[u8 resultado][u16 puntos de nivel][u16 puntos PowerUser][u8 estadística][u16 valor nuevo]`. **Observed**
    (`engine.dll` 0x361938A0, `rakion.bin` 0x47DBB0).
  - Reglas del original (0x40B3D0): sin puntos → 3; estadística en su tope → 4; índice > 9 → 5; gasta primero un
    punto de nivel y si no hay uno de PowerUser.
  - Los campos de estadística del personaje son **contadores de puntos asignados** (el número de la izquierda en
    "(n/50)"), no totales. El tope real sale de una tabla por clase aún **Unknown**; se usa 50 (**Inferred** de la
    interfaz).
  - Puntos por nivel: 3 por cada nivel subido (dato aportado por el mantenedor; se aplicará al implementar la subida
    de nivel en la fase 6).
- PU Card (`0x0034`):
  - Petición `[u8 renovar][u8 cupón]` + `[u16 slot del cupón]` si hay cupón; respuesta `[u8 resultado][u32 oro][u32 cash]
    [u32 caducidad PowerUser][u16 puntos PowerUser][u8 M]` + M × `u32` (**Unknown**, vacío). **Observed** (`engine.dll`
    0x361958C0, `rakion.bin` 0x474F50).
  - Reglas del original (0x40B2C0 y SQL de `DBCommandInventoryBuyPowerUser`, **Observed**): renovar = 6000 cash y
    caducidad + 30 días; nueva = 8000 cash y ahora + 30 días; +5 puntos PowerUser; si la caducidad ya está a más de
    180 días, resultado 4; sin cash, resultado 3. La caducidad usa la unidad de `LegacyGameClock`.
  - Diferencia propia: renovar una tarjeta ya caducada empieza desde ahora.
  - Pendiente: el estado PowerUser no se envía todavía en el login, así que tras volver a entrar el cliente puede no
    mostrarlo hasta localizar ese campo del paquete de login (fase 3, carril B).
- Herrería (`0x0074`):
  - Petición `[u8 slot del ítem][u8 slot de la piedra][u8 N ≤ 3][N × u8 slots de material]` (**Observed**; el
    original desconecta si N > 3). Respuesta `[u8 resultado]` y, si es menor que 6, `[u8 ítem][u8 piedra][u8 N][N × u8]`.
  - Resultados (cliente `rakion.bin` 0x478D80): 0 sube 1, 1 sin cambio, 2/3/4 baja 1/2/3, 5 se rompe; con 0–5 el
    cliente quita la piedra y los materiales. 6 no refinable o en +15, 7 piedra/material inválido, 8 piedra no válida
    para ese nivel (original 0x40C310). **Observed**; significados **Derived**.
  - Reglas del original: el ítem debe ser equipo (id < 8000) y estar por debajo de +15; piedra 13001 solo por debajo
    de +5; 13004 solo desde +10. La rotura nunca ocurre: el original la convierte en −3 (nivel ≤ 3) o en "sin cambio".
  - Probabilidades: el original las lee de una tabla de configuración por piedra y materiales que no está en el
    binario (**Unknown**). `EnchantOdds.DefaultFor` es provisional (**Inferred**): 100 % hasta +3 y después menos
    éxito y algo de −1. Se puede sustituir al construir `BlacksmithModule`.
- Reseteo de puntos (`0x001B`, 4.7):
  - Petición `[u8 usa cupón][u16 slot del cupón]`; respuesta `[u8 resultado][u32 cash][u16 puntos][u16 puntos PU][u8 N][N × u32 regalos]` (cliente `engine.dll` 0x36195400, **Observed**). El servidor responde por `0x001B`, el mismo número que la confirmación de GameGuard.
  - Precio en cash según nivel: 7000 hasta 15, 12000 hasta 40, 19000 por encima (`RakionWorldServ.exe` 0x413FD4, **Observed**). Resultado 1 sin cash, 2 sin puntos repartidos.
  - El personaje se queda con 3 puntos por nivel por encima de 1 y el resto vuelve a los puntos PowerUser de la cuenta (**Derived** de 0x414043).
  - Cupones de descuento (ítems 11000–11999) rechazados con 0x14: su tabla de descuentos no está en el binario (**Unknown**). La lotería de regalos del original no se envía.
- Bolsa (`0x0032`, 4.8): 8000 de cash (0x415033, **Observed**), máximo 4 bolsas (**Derived**: 120 slots del box, 30 por bolsa); resultado 3 sin cash, 2 en el máximo. Respuesta `[u8 resultado][u32 oro][u32 cash][u8 bolsas][u8 0]` (orden oro/cash **Inferred**).
- Slot de poción (`0x006F`, 4.8): de 3 a 6 por personaje, con el precio de los ítems de tienda 10008, 10009 y 10010 según los slots actuales (0x428E02, **Observed**); resultado 4 sin dinero. La ficha de cada personaje envía sus slots reales.
- Compras de Stage (6.7), respuesta `[resultado][oro][cash][0]` sin byte de cantidad (`engine.dll` 0x36195BD0 / 0x36195CC0, **Observed**):
  - Borrar rangos (`0x0070`): borra todos los rangos de Stage del personaje. Precio en cash según el nivel: 10–20 ítem 10011, 21–40 ítem 10012, 41+ ítem 10013 (2900 / 6400 / 9900 si el catálogo no los tiene); por debajo de nivel 10 el original no cobra (0x429323, **Observed**).
  - Acceso libre por nivel (`0x0071`): ítem 10014 (16 500 de cash), dura 24 h (1440 minutos, 0x41871E). No se vuelve a vender mientras siga activo (resultado 3); se guarda en `user_stage_level_free` y se carga al iniciar sesión. El cliente decide por su cuenta si la lista de stages respeta el nivel: lo sabe por la hora de compra que va en el último `u32` del bloque de cuenta del `0x000D` (`rakion.bin` 0x47E781 → AccountInfo+0x2D98 → 0x47E7A7 → 0x46553F, **Observed**). El `0x0071` no la lleva, así que tras comprar hay que volver a iniciar sesión para que el cliente lo note (como en el original, **Inferred**).
  - Resultado 2: no hay cash suficiente.
- Pociones: al comprarlas, la cantidad (ítems 12000–12999) se guarda en `exp`, como el original (0x4174E9, **Observed**); sale de la columna `hit2` del catálogo (cargador 0x40A670, **Observed**). El gasto en partida (`0x006E`) todavía no se descuenta.
- Pendientes de 4.8: comprar slot de personaje (el login muestra como máximo 3 personajes, hay que revisarlo antes) y apilar pociones (`0x0073`, falta saber cómo se guardan las cantidades).
