# Rakion.Broker

Servidor de Handshake, Autenticación y Lista de Servidores para el emulador `rakion-brokenyx` (Rakion v258).

## Propósito y Rol

`Rakion.Broker` reemplaza el antiguo `BrokenServer.exe` de la comunidad y la suite PHP externa (`fetch.php`), implementando de forma limpia, asíncrona y fuertemente tipada:

1. **Listener TCP en puerto 40706:** Punto de entrada de red inicial cuando el cliente `rakion.bin` arranca.
2. **Entrega de Lista de Servidores (Opcode `0xCDDF` -> `0x0101`):** Retorna la lista estructurada de mundos disponibles (`GameServers`), direcciones IP dinámicas, puertos (`40708`), salas en uso y límites de jugadores.
3. **Autenticación Limpia de Usuarios (Opcode `0x1310` / `0x3000` / `0x4000`):**
   - Descifra dinámicamente el usuario real enviado por `NyxLauncher.exe` (AES-128-ECB con clave estática `0x464FA0`).
   - Verifica el desafío de contraseña con derivación de clave de sesión `SHA-0` (`SHA-0(Password + Username + Salt)`), admitiendo contraseñas en texto plano y migración transparente de hashes heredados (MD5 o BCrypt).
   - Maneja reintentos de inicio de sesión sobre la misma conexión TCP soportando el handshake secundario `0x3000` bajo variantes de trama como `0xCDDF` y `0x650D`.
   - Valida la existencia de la cuenta contra la base de datos MySQL/MariaDB (`user`) y comprueba el estado de baneo (`bandate` / `user_game_info.is_banned`).
   - Vincula el usuario autenticado a la conexión TCP para que el posterior paquete `0x4000` ("Start Game") emita el Session Ticket al usuario real (no fijado a `"admin"`).
   - Responde con el código de resultado correspondiente (`LoginResultCode.Success`, `InvalidCredentials`, `AccountBanned`).
4. **Gestor de Tickets de Sesión (`ISessionTicketService`):**
   - Genera tokens de sesión únicos en memoria con tiempo de vida limitado (TTL de 5 minutos).
   - Estos tokens son consumidos posteriormente por `Rakion.World` en el puerto 40708 para asociar el socket al usuario autenticado sin reenviar contraseñas por la red.
5. **Mini Servidor HTTP AutoFetch (Reemplazo de `fetch.php`):**
   - Expone un endpoint HTTP interno compatible con el protocolo plano de Softnyx (`GET /fetch/fetch.php?11001&...`) para permitir que el lanzador oficial (`NyxLauncher.exe`) valide la versión del cliente e inicie el juego sin requerir un servidor web Apache/PHP independiente.

## Opcodes Implementados

| Opcode | Tipo | Nombre | Descripción |
| :--- | :--- | :--- | :--- |
| `0xCDDF` | Entrada | `ServerListRequest` | Petición de lista de servidores desde el cliente. |
| `0x0101` | Salida | `ServerListResponse` | Envío de servidores activos con IP, puerto y cupos. |
| `0x1310` | Entrada | `SV_AUTH_LOGIN` | Autenticación estándar v258. |
| `0x3000` | Entrada | `SV_AUTH_LOGIN_2` | Variante alternativa de login. |
| `0x4000` | Entrada | `SV_AUTH_LOGIN_4` | Variante de login con ticket extendido. |
| `0xCBEB` | Salida | `SV_AUTH_LOGIN_RESP` | Respuesta de auth estándar (Sub-opcode `0x1312` + ResultCode). |
| `0xEBD3` | Salida | `SV_AUTH_LOGIN_RESP_2` | Respuesta de auth variante 2 (Sub-opcode `0x3001` + ResultCode). |
| `0x1BAF` | Salida | `SV_AUTH_LOGIN_RESP_4` | Respuesta de auth variante 4 (Sub-opcode `0x4001` + ResultCode + Ticket). |
