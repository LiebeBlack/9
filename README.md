# NetForge Studio

**Convierte cualquier PC con Windows en un router local (intranet) con transferencia de archivos, y funciona sin internet.**

NetForge Studio levanta una red Wi-Fi propia, muestra quién entra, permite bloquear intrusos al instante y comparte archivos con cualquier móvil escaneando un código QR. Todo desde un ejecutable único, sin instalador, sin dependencias y sin servicios en la nube.

![estado](https://img.shields.io/badge/estado-v1.0.0-blue) ![plataforma](https://img.shields.io/badge/plataforma-Windows%2010%201903%2B-lightgrey) ![runtime](https://img.shields.io/badge/.NET%20Framework-4.8-512bd4) ![licencia](https://img.shields.io/badge/licencia-GPL--3.0-blue)

**Sitio del proyecto:** [`site/index.html`](site/index.html) es una página autocontenida (sin dependencias, sin recursos externos) con toda esta documentación integrada: funciones, motores, compatibilidad, límites conocidos y verificación, en español y en inglés, más una simulación del radar y del Martillo. Se publica en GitHub Pages con el flujo `sitio`.

---

## Por qué existe

Convertir un PC en punto de acceso es fácil hasta que Windows decide lo contrario: si no hay internet, el asistente de "zona con cobertura móvil" se niega a arrancar; si el driver no implementa Soft AP, `netsh hostednetwork` falla sin explicar nada; si el móvil se descuelga, la red desaparece y no vuelve. NetForge Studio ataca esos tres problemas concretos:

1. **Modo Off-Grid**: usa Wi-Fi Direct en modo grupo autónomo (`WiFiDirectAdvertisementPublisher` con `LegacySettings`), que emite un SSID normal para cualquier teléfono **sin exigir conexión a internet**. Los clientes reciben dirección del propio grupo, así que la intranet funciona en medio del campo.
2. **Motor dual adaptativo**: sondea en runtime qué puede hacer *tu* equipo (Wi-Fi Direct, punto de acceso moderno, red hospedada clásica) y elige motor solo. Nada de listas de adaptadores compatibles ni de nombres de interfaz fijos.
3. **Auto-resurrección (Daemon Guard)**: vigila la red cada 2 segundos y la vuelve a levantar si el driver cae, el adaptador se suspende o el sistema se reanuda. Una red caída se recupera sola en segundos.

## Características

| Función | Qué hace |
|---|---|
| **Red local 1 clic** | SSID y clave propios (clave aleatoria segura por defecto). Bandas 2.4 y 5 GHz. |
| **Radar en vivo** | Audita la red cada 3 s en hilo de fondo: IP, MAC, fabricante real (base OUI del IEEE offline con prefijos de 24, 28 y 36 bits) y nombre del equipo. |
| **El Martillo** | Bloquea un dispositivo con un clic (regla de firewall entrante y saliente). La identidad es la **MAC**, así que si el intruso reinicia el móvil para pedir otra IP, sigue bloqueado. |
| **Gestor de Indultos** | Panel de la lista negra para retirar el bloqueo con un clic, y verificación automática de que la regla siga puesta. |
| **Portal web de archivos** | Carpeta compartida accesible desde el navegador del móvil: subida múltiple en flujo continuo (películas de varios GB sin pasar por memoria) y descarga con `Range`, para poder ver vídeo directamente. |
| **Servidor FTP** | `ftp://IP:2121/` en modo pasivo, con jaula de rutas y solo-descarga por defecto. |
| **Código QR** | Codificador QR propio (Reed-Solomon, elección de máscara) para entrar sin teclear nada. |
| **Compartir internet (ICS)** | Opcional, nunca automático: reparte la conexión si la hay y **la desactiva siempre** al parar. |
| **Modo Fantasma** | La "X" no cierra: destruye la interfaz, compacta la memoria y deja la red y los archivos funcionando desde la bandeja del sistema. |
| **Arranque silencioso** | Tarea programada con privilegios altos: arranca con Windows **sin pedir permisos cada vez**. |
| **Diagnóstico** | Panel copiable con lo que este equipo puede y no puede hacer, más registro rotado en `%LOCALAPPDATA%\NetForge\logs`. |

## Requisitos

- Windows 10 build 18362 (1903) o superior. En versiones anteriores la aplicación avisa y funciona a lo sumo con el motor heredado.
- .NET Framework 4.8 (viene de serie en Windows 10 1903+ y Windows 11).
- Permisos de administrador: el manifiesto usa `requireAdministrator` y Windows pide el aviso de UAC una sola vez al abrir. **No se implementa ningún truco para saltarse el UAC**, porque es técnica de malware y rompe el modelo de seguridad del sistema.
- Nada más: no hay instalador, ni dependencias, ni conexión a internet.

## Uso rápido

1. Ejecuta `NetForge.exe` (acepta el aviso de UAC).
2. Escribe el nombre de la red y la clave, pulsa **Crear red**.
3. En la pestaña **Archivos**, elige la carpeta y pulsa **Iniciar servidores**.
4. Escanea el QR con el móvil: se abre el portal web. Los archivos suben y bajan a velocidad de red local.
5. Cierra la ventana: la app pasa al Modo Fantasma y la red sigue activa. Doble clic en el icono de la bandeja para volver. Si no quieres escanear el QR, **Copiar enlace** deja la dirección del portal en el portapapeles.

Para apagarlo del todo, usa **Salir del todo** en el menú de la bandeja: avisa antes, porque cierra la red y los servidores de archivos.

## Compilar

No hace falta Visual Studio, ni .NET SDK, ni permisos de administrador: el script descarga un compilador Roslyn de NuGet y compila con las bibliotecas del propio Windows.

```powershell
./build.ps1              # compila, ejecuta la autocomprobación y deja bin\NetForge.exe
./build.ps1 -SkipData    # no regenera la base de datos OUI ni el icono (usa los del repositorio)
./scripts/check-qr.ps1   # verifica el código QR con un lector independiente (ZXing)
./scripts/check-site.ps1 # comprueba el sitio: traducciones pareadas y cero recursos externos
./scripts/package.ps1    # compila y deja dist\NetForge-Studio-<versión>-portable.zip listo para repartir
```

El paquete portable lleva el ejecutable, la licencia, el historial, la documentación y un `LEEME.txt` con las instrucciones. El propio script abre el ZIP al terminar y comprueba lo que hay dentro: si falta la licencia o se ha colado un `.dll`, falla en lugar de repartir algo incompleto. Lo mismo se verifica en cada pull request, y al empujar una etiqueta `v*` se publica solo como entrega de GitHub.

Salidas en `bin\`:

- `NetForge.exe` — la aplicación (pide administrador).
- `NetForge.Autotest.exe` — autocomprobación sin privilegios (195 comprobaciones).

## Verificación

```powershell
./bin/NetForge.Autotest.exe
```

Comprueba la lógica pura sin tocar el sistema: validación de SSID y clave (incluida la longitud en bytes UTF-8 y el rechazo de comillas), matemáticas de subred, jaula de rutas del portal y del FTP, lector de formularios multipart con un caso malicioso, Reed-Solomon del QR, nombres de regla del firewall y la máquina de estados del Daemon Guard contra un motor falso. Además contrasta las direcciones de su propia tabla ARP con las de `arp -a` del sistema, que es como se detectó un fallo real de orden de bytes.

Sin salir del equipo de pruebas, y sin permisos de administrador, también se verifica de verdad:

- **El servidor FTP y el portal web**, levantados en `127.0.0.1` sobre una carpeta temporal: `LIST`, `RETR`, `STOR`, `SIZE`, borrado prohibido, descarga por HTTP, petición `Range` con respuesta 206, subida multipart y el intento de salir de la carpeta con `..%2F`.
- **El radar**, con proveedores inyectados: identidad por MAC, nombre confirmado por el motor, fabricante resuelto con la base OUI, MAC aleatorizada detectada, cliente sin MAC descartado y parada limpia.
- **La ventana real, en español y en inglés**: se construye el panel, se recorren sus cinco pestañas y se pinta cada una a un mapa de bits (una pestaña en blanco cuenta como fallo). Se comprueba además que ningún texto visible se quedó sin traducir, que el Modo Fantasma destruye la interfaz y que volver a abrirla desde la bandeja la reconstruye.
- **Los caminos de fallo de los motores**: un arranque a medias que debe deshacerse sin dejar la radio emitiendo, y una parada que Windows no confirma, que no puede declararse como "red apagada".

## Documentación

- [`site/index.html`](site/index.html) — **el sitio del proyecto**, con toda la documentación reunida y en dos idiomas.
- [`docs/arquitectura.md`](docs/arquitectura.md) — cómo está construido y por qué.
- [`docs/compatibilidad.md`](docs/compatibilidad.md) — matriz de capacidades por hardware y Windows.
- [`docs/limites-conocidos.md`](docs/limites-conocidos.md) — **lo que este programa no puede hacer**, sin adornos.

## English summary

NetForge Studio turns any Windows 10 1903+ PC into a local Wi-Fi router with file transfer, and it works with **no internet at all**. It uses Wi-Fi Direct autonomous group owner mode (plus modern hotspot and legacy hosted-network engines, selected at runtime from a capability probe) to broadcast an SSID that phones see as an ordinary network. A watchdog resurrects the network within seconds if the driver collapses. A live radar lists connected devices with vendor lookup from a bundled offline IEEE OUI database, one-click MAC-keyed firewall bans, a web portal with streaming uploads and range downloads, an FTP server, and a self-contained QR encoder. Closing the window destroys the UI to free memory while the network and file servers keep running from the tray. Single executable, no installer, no third-party runtime dependencies, GPL-3.0.

## Licencia

GPL-3.0-or-later. Ver [`LICENSE`](LICENSE).
