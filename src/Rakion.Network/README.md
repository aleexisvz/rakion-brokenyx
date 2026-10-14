# Rakion.Network

`Rakion.Network` proporciona la infraestructura de transporte asíncrono para el emulador `rakion-brokenyx`. Implementa servidores TCP y UDP optimizados para alta concurrencia, enmarcado de paquetes sin copias innecesarias mediante `System.IO.Pipelines` y gestión del ciclo de vida de conexiones.

## Responsabilidades y Alcance

- **Enmarcado de Paquetes (Message Framing):** En TCP, los datos llegan como un flujo continuo (*stream*) que puede fragmentarse o unirse. `Rakion.Network` lee los 4 bytes de cabecera (`ushort TotalLength`, `ushort Opcode`) y garantiza que los manejadores solo reciban paquetes completos y válidos.
- **Sin Bloqueos (Non-blocking I/O):** Utiliza `System.IO.Pipelines.Pipe` para desacoplar la lectura de la tarjeta de red del procesamiento lógico del paquete.
- **Protección contra Sobrecargas:** Valida los tamaños de paquetes declarados por el cliente antes de procesar la memoria para evitar ataques de denegación de servicio (DoS) por paquetes malformados.

## Contenido del Módulo

### 1. Servidor y Conexión TCP (`Rakion.Network.Tcp`)
- **`TcpServer`:**
  - Enlaza un socket TCP no bloqueante en la IP y puerto configurados (por ejemplo, `0.0.0.0:40706` para el Broker o `0.0.0.0:40708` para el World).
  - Bucle de aceptación asíncrono con control de concurrencia y apagado suave (*graceful shutdown*).
  - Administra la colección segura de conexiones activas.
- **`TcpConnection`:**
  - Encapsula el socket del cliente conectado (`Socket`).
  - Bomba de lectura con `PipeWriter` y bucle de enmarcado con `PipeReader`.
  - Envío concurrente protegido por semáforo asíncrono (`SendAsync`).
  - Notificación de eventos (`OnConnectedAsync`, `OnPacketReceivedAsync`, `OnDisconnectedAsync`).
- **`ITcpPacketHandler`:**
  - Contrato que implementan el Broker Server o el World Server para reaccionar a la conexión de clientes y al despacho de paquetes deserializados.
