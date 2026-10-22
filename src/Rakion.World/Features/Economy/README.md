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
| 4.1 Carga de inventario | `InventoryEnter` `0x002C`, `InventoryLeave` `0x002D` (**Observed**) | Petición recibida; los items de la cuenta se cargan de BD. **No se envía respuesta**: el layout de la lista de inventario es **Unknown**. | no |
| 4.2 – 4.6 | ver `ClientOpcode` (`0x002E`–`0x0035`, `0x006B`–`0x0074`, **Observed**) | Pendiente | no |

## Evidencia y decisiones

- Opcodes: leídos del inmediato con el que cada `IScavengerWorldNet::Send*` de `engine.dll`
  (v258) escribe el opcode. El método se validó con `SendAlive` (`0x000F`) y
  `SendCharacterStateClear` (`0x001B`), que ya estaban confirmados.
- El inventario se carga por cuenta (`characterId: null`): **Inferred** de la columna
  `character_id` (0 = compartido) del esquema heredado.
- Pendiente para cerrar 4.1: observar en el cliente el parser de la respuesta a `0x002C`
  (requiere un desensamblador configurado en `rea`).
