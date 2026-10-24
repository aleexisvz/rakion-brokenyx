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

## Pendiente

- Comprobar con el cliente real que la lista de amigos aparece en la ventana del Messenger.
- Mensajes (`0x2030` y entrega `0x2010`) y grupos.
- Seguridad: el login original no prueba la contraseña. El puerto debe quedarse en una red de confianza hasta que el World emita un token para el Messenger.
