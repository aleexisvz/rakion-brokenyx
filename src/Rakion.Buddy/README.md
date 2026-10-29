# Rakion.Buddy

Servidor del Messenger (amigos) que usa la librería `Buddy2.dll` del cliente. Escucha TCP y UDP en el mismo puerto: el cliente lo lee de `BuddyIP` / `BuddyPort` en `DataSetup/Locale.ini` (8500 en el cliente v258, **Observed**). Se configura en la sección `Buddy` de `config/appsettings.json` y lo arranca `Rakion.App`.

## Etapa 1 (implementada)

- Trama `[u16 tamaño][u16 comando][datos]`, igual que el resto de servidores.
- Login: `0x1000` → `0x1001 [u32 seed][u32 cookie]`; `0x1010` de 208 bytes = credencial AES-128-ECB con clave fija (cuenta y seed) + 132 bytes de datos en bloques `AES(clave de sesión, [0x2DBABE65][12 bytes])`, con clave de sesión = SHA-0(cuenta + seed). Tamaños, marcador y cabecera `0x1B` **Observed** en `Buddy2.dll` 0x100077F9; la clave fija y el formato de los bloques son **Inferred** (`Rakion.Core/Buddy/BuddyCrypto.cs`).
- `0x1011 [u16 resultado][u32 token UDP][u16 n][n × 148 bytes]`: lista de amigos (cuenta, nombre visible y grupo).
- Registro UDP: el cliente envía el token de 4 bytes; el servidor guarda su dirección, responde `0x101F` y avisa a los amigos conectados con `0x3FFF` (en línea / desconectado).
- Túnel (`0x2020` → `0x2021`): reenvía mensajes entre clientes cuando el UDP directo no llega; entre quienes todavía no son amigos solo pasan la solicitud de amistad y su respuesta (`0xC041` / `0xC042`, **Inferred**). Formato **Observed** en `Buddy2.dll` 0x10008784 y 0x10007D43. Así viaja la solicitud de amistad al otro jugador.
- Agregar (`0x3000`) y quitar (`0x3002`) amigos, siempre en los dos sentidos; nick (`0x3100`, se guarda como nombre visible), gremio y extensión (`0x3102` / `0x3104`, solo responden) y lista de grupos vacía (`0x3150`).
- Nombre visible: el nick del Messenger, si no el primer personaje, si no el login.

## Límite del cliente v258: agregar amigos no sale del cliente

El objeto de `Buddy2.dll` guarda en `+0x140D4` una máscara de servicios. El constructor la pone a `1` (0x10001547) y ningún binario del cliente la vuelve a escribir: no hay otra referencia a ese desplazamiento en `Buddy2.dll`, `engine.dll`, `gamemp.dll` ni `rakion.bin` (**Observed**). Cada método de la vtable `0x100204D0` comprueba un bit de la máscara; si el bit está a 0, devuelve «éxito» **sin enviar nada** (**Observed**). Con la máscara a 1 son no-ops, entre otros:

- solicitud de amistad P2P `0xC041` (método `+0x38`);
- aceptar `0xC042`, que después envía `0x3000` (`+0x3C`);
- quitar `0xC043` / `0x3002` (`+0x40`);
- consulta de estado `0x3010` (`+0x44`) y `0x3006` (`+0x5C`).

Así se explica lo que se vio con el cliente real: tras `0x19 GetUserName`, el cliente muestra «Sending message…» y no llega nada al servidor, ni por TCP ni por UDP. Los `0x3010` y `0x2020` de una prueba anterior salieron por otra ruta sin máscara: `OnUserUnreachable` (0x1000A520) y el reenvío `0x2000` / túnel (0x10008640, 0x1000A490), que se usan cuando falla un envío P2P.

Sin modificar el cliente, las amistades tienen que crearse fuera del Messenger: por ejemplo con un comando del World o con la futura web. El servidor ya entrega la lista en `0x1011`.

## Pendiente

- Comprobar con el cliente real que la lista de amigos aparece en la ventana del Messenger.
- **Agregar amigos en vivo (pospuesto).** Idea sin parche, sin probar: usar el `0x19 GetUserName` del World como intención de agregar, crear la amistad en el servidor y avisar a los dos clientes con un paquete que acepten sin pedirlo (`0x3001`, `0x3FFF` o `0x2021` con `0xC041`). Falta analizar esos lectores (0x10007CD8 y siguientes) y confirmar que `0x19` solo lo usa la ventana del Messenger (**Unknown**). La amistad sería automática, porque aceptar tampoco sale del cliente.
- Mensajes (`0x2030` y entrega `0x2010`) y grupos.
- Seguridad: el login original no prueba la contraseña. El puerto debe quedarse en una red de confianza hasta que el World emita un token para el Messenger.
