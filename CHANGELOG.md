# Historial de cambios

El formato sigue [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/) y el proyecto usa [versionado semántico](https://semver.org/lang/es/).

## [1.0.0] - 2026-09-21

Primera versión completa y funcional.

### Añadido

- **Sitio del proyecto** ([`site/index.html`](site/index.html)): una sola página autocontenida, sin dependencias ni recursos externos, con toda la documentación integrada (funciones, motores, compatibilidad, límites conocidos, verificación y guía de compilación), en español e inglés, con simulador del radar y del Martillo. Se publica en GitHub Pages al cambiar `site/`.
- **Comprobación del sitio** (`scripts/check-site.ps1`): detecta textos con un solo idioma, recursos externos prohibidos y secciones que falten antes de publicar, también en cada pull request.
- **Guardia de idioma en la aplicación**: la ventana se monta en español y en inglés dentro de la autocomprobación, y cualquier texto visible que sea una clave sin traducir o una cadena escrita a mano en español hace fallar la comprobación. Se descubrió así que tres etiquetas («apagado», «no se pudo generar el código» y el aviso de falta de permisos) estaban puestas a mano y no cambiaban de idioma.
- **Ajustes reales y persistentes**: idioma de la interfaz, banda preferida, intervalos del radar y del Daemon Guard, puertos del portal y del FTP, usuario del FTP (vacío significa acceso anónimo), levantar los servidores de archivos al crear la red, avisos en la bandeja y aviso cuando entra un dispositivo nuevo. Todo se guarda saneado y se aplica al vuelo, con botones de guardar siempre visibles.
- **Menú contextual en Clientes**: banear, indultar, copiar la IP o la MAC y abrir el portal en el navegador, con las opciones activadas solo cuando toca.
- **Servicio de uso compartido visible y reparable**: la pestaña de Ajustes muestra el estado real del servicio de Windows que necesita el reparto de internet y permite arrancarlo; la tarea programada de arranque con Windows se consulta y se puede reparar desde la propia aplicación.
- **Capturas de revisión de la interfaz**: la autocomprobación puede guardar cada pestaña como PNG, incluida la vista con el panel desplazado al fondo, para revisar el diseño sin abrir la aplicación.

- **Motor dual adaptativo** con sondeo de capacidades en runtime y cadena de respaldo: Wi-Fi Direct en grupo autónomo, punto de acceso moderno de Windows y red hospedada clásica.
- **Modo off-grid**: la red se levanta sin conexión a internet usando el grupo autónomo con `LegacySettings`, de forma que los móviles ven un SSID normal.
- **Daemon Guard**: vigilancia cada 2 s, resurrección automática tras caída del driver o suspensión, espera creciente contra tormentas de reinicios, sin solapamiento entre comprobaciones y respeto absoluto a la detención manual.
- **Radar en vivo** cada 3 s sobre la tabla ARP del sistema, con refresco activo de equipos conocidos, nombres de host, confirmación por el motor cuando la ofrece y fabricante real desde una base OUI del IEEE incrustada (53.985 prefijos de 24, 28 y 36 bits, con detección de MAC aleatorizada).
- **El Martillo**: bloqueo con un clic mediante reglas de firewall entrante y saliente, identidad por MAC y reaplicación automática si el dispositivo cambia de dirección.
- **Gestor de Indultos**: panel de la lista negra persistida, indulto individual y general, y restauración automática de reglas que hayan desaparecido.
- **Portal web de archivos** con subida múltiple en flujo continuo, descarga con soporte de `Range` para ver vídeo, borrado opcional y página sin recursos externos.
- **Servidor FTP** en modo pasivo con solo-descarga por defecto.
- **Codificador QR propio** (Reed-Solomon, elección de máscara por penalización, versiones 1 a 10, cuatro niveles de corrección) sin dependencias.
- **Compartir internet** opcional mediante el servicio de uso compartido de Windows, con desmontaje automático al parar.
- **Modo Fantasma**: la ventana se destruye para liberar memoria y la red, el radar, el firewall y los servidores de archivos siguen funcionando desde la bandeja. El consumo real se muestra en el diagnóstico.
- **Arranque con Windows** mediante tarea programada con privilegios altos, para no pedir UAC en cada inicio de sesión.
- **Interfaz en español e inglés** con tema oscuro, panel de diagnóstico copiable, lista negra con estado vacío explicado, botón para copiar el enlace del portal y registro rotado en `%LOCALAPPDATA%\NetForge\logs`.
- **Salida protegida**: "Salir del todo" desde la bandeja pregunta antes de cerrar la red y los servidores de archivos, para que un clic de más no deje a los móviles sin red.
- **Paquete portable** reproducible (`scripts/package.ps1`) que se comprueba a sí mismo leyendo el ZIP recién creado, publicado como entrega de GitHub al empujar una etiqueta `v*`.

### Arreglado tras la primera revisión

- El paquete portable usaba `Compress-Archive`, que guarda las rutas con barra invertida: el ZIP solo lo abría el Explorador de Windows y en Linux, macOS o un móvil aparecía como un fichero con un nombre imposible. Ahora las entradas se escriben con barras normales y el propio script rechaza el paquete si vuelve a pasar.
- El paquete se verifica descomprimiéndolo y comparando el tamaño del ejecutable extraído con el compilado: un ZIP que se genera pero no se puede restaurar ya no se publica.
- **Autocomprobación** de 195 comprobaciones ejecutable sin privilegios (incluye el FTP y el portal levantados de verdad en `127.0.0.1`, el radar con proveedores inyectados y el pintado de la ventana real en español y en inglés, con detección de cualquier texto que se quede sin traducir), y verificación del código QR con el lector independiente ZXing durante la integración continua.

### Corregido durante el desarrollo

- Conversión de direcciones de la tabla ARP: se usaba el orden de bytes de la máquina, lo que convertía 192.168.1.1 en 1.1.168.192 y arruinaba el cálculo de subred del sondeo activo. Se detectó contrastando con `arp -a` y ahora hay una comprobación que lo vigila.
- Lector de formularios multipart: un delimitador que aparece a medias dentro del contenido de un archivo se tomaba por fin de parte. Ahora se trata como datos, con una comprobación que lo cubre.
- Jaula de rutas del portal: una ruta con barra inicial se rechazaba por accidente al recombinarla; ahora se rechaza de forma explícita y comprobada.
- La autocomprobación se separó en un ejecutable propio sin manifiesto de administrador, porque el principal no puede iniciarse sin UAC y eso impedía verificarlo en integración continua.
- Servidor FTP: el canal pasivo enviaba el texto de la respuesta final antes de cerrar la conexión de datos, de modo que un cliente estricto leía una línea de más. Ahora el orden es 150, datos, cierre, 226.
- Registro en fichero: las escrituras desde el radar y desde la interfaz podían coincidir y perder la línea que llegaba segunda. Ahora van en serie.
- Motores de red: un fallo **después** de encender la radio dejaba el adaptador publicando y el controlador probaba el siguiente motor con el anterior todavía emitiendo. Ahora un arranque a medias se deshace antes de devolver el error.
- Motores de red: si Windows no confirmaba la parada, la aplicación decía "red apagada" con el punto de acceso posiblemente encendido. Ahora se conserva el gestor, se informa del fallo y el estado sigue mostrando la red como activa hasta que la parada se confirme.
- Compartir internet: una activación interrumpida a la mitad (lado privado activado, lado público fallido) quedaba aplicada sin que la aplicación lo supiera, y por tanto no se desactivaba nunca. Ahora se deshace en el acto.
- Radar: una pasada en vuelo podía publicar la lista de clientes después de parar o liberar el servicio. Ahora se descarta.

[1.0.0]: https://github.com/TU-USUARIO/netforge-studio/releases/tag/v1.0.0
