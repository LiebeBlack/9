# Seguridad

## Cómo informar

Abre un informe privado a través de la pestaña **Security** del repositorio, o un informe de error normal si el problema no permite atacar a nadie. Intentaré responder en una semana.

## Qué se considera un problema de seguridad

- Escaparse de la **carpeta compartida** desde el portal web o el FTP (por ejemplo, leer `C:\Windows\System32`).
- Conseguir ejecutar código o inyectar comandos a través del SSID, la clave o los nombres de archivo.
- Que los servidores de archivos queden accesibles **fuera de la red local** (por Ethernet, VPN u otro adaptador).
- Que un archivo subido pueda sobrescribir archivos existentes de la carpeta compartida.
- Que el uso compartido de conexión quede activado después de cerrar la aplicación o de detener la red.

## Modelo de seguridad: qué protege y qué no

**Protege:**

- La carpeta compartida está enjaulada: cada ruta se resuelve y se comprueba que siga dentro. Las rutas absolutas se rechazan.
- Los servidores escuchan únicamente en la dirección del adaptador del punto de acceso.
- Por defecto solo se puede descargar; subir y borrar son opciones explícitas del usuario.
- El SSID y la clave se validan antes de tocar la línea de comandos de `netsh`.
- La clave se genera con el generador criptográfico del sistema en el primer arranque.

**No protege (y no es un fallo):**

- **No hay cifrado propio.** La confidencialidad de la red depende de WPA2 con la clave que elijas. Con la red levantada, los dispositivos pueden hablar entre ellos: es una intranet, no una red segregada.
- **No bloquea el tráfico entre dos clientes** del mismo punto de acceso. Eso ocurre en la capa 2 y el firewall del PC no lo ve (ver `docs/limites-conocidos.md`).
- **El panel de diagnóstico y el registro son texto abierto** en `%LOCALAPPDATA%\NetForge`. Contienen la clave de la red, porque es una herramienta de administración local; no los compartas en informes públicos sin revisarlos.

## Sobre la elevación de privilegios

La aplicación se declara con `requireAdministrator` en su manifiesto: Windows pide consentimiento una vez y la aplicación nunca falla por permisos. **No se implementa ninguna técnica para evitar el aviso de UAC**; si en algún cambio se introdujera, se rechazaría.
