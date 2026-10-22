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
| 4.1 Carga de inventario | `InventoryEnter` `0x002C`, `InventoryLeave` `0x002D` (**Observed**) | Responde `0x002C` con `[u8 Result][u32 ServerTimeMinutes]` (**Observed**). El Box de la cuenta (120 slots) viaja en la cola de `WorldLoginPart2` (`0x000D`) y lo llena `AccountInventoryProvider`. Los 19 slots de equipo por personaje están pendientes. | `0x002C`: sí. Box: pendiente de probar |
| 4.2 – 4.6 | ver `ClientOpcode` (`0x002E`–`0x0035`, `0x006B`–`0x0074`, **Observed**) | Pendiente | no |

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
    un `u8` (**Unknown**), 120 × `u16` itemId, 120 × `u8` level, 120 × `u32`, 120 × `u32` y un `u32`
    final (**Unknown**). **Observed.**
  - Que los dos arrays de `u32` sean exp y limittime es **Derived** del orden de copia del servidor
    original (`RakionWorldServ.exe` 0x412065..0x4120E6).
  - Reglas del servidor original (`DBCommandCharacterList`), aplicadas igual: las filas con
    `characterid = 0` van al slot indicado por `slot`; los alquileres con `limittime` ya pasado se
    listan como caducados; las filas de personaje no entran en el Box.
  - Diferencias propias: se ignoran (con aviso en el log) los slots fuera de 0..119 o repetidos y los
    itemId que no caben en `u16`.
  - `user_items.limit_time_minutes` debe guardarse en la unidad de `LegacyGameClock` (0 = permanente).
