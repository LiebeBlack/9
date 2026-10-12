# Arquitectura

## Principio rector

La red es lo importante; la interfaz es prescindible. Todo lo que mantiene la red viva (motor, vigilancia, radar, firewall, servidores de archivos) vive en el **contexto de aplicación**, no en la ventana. La ventana se puede destruir y recrear sin que la red se entere: eso es el Modo Fantasma, no un truco de ocultación.

## Mapa de módulos

```
site/
  index.html              El sitio del proyecto: una página autocontenida, bilingüe y sin
                          dependencias, con toda la documentación y una simulación del radar

src/
  Program.cs              Instancia única, arranque silencioso, captura global de errores
  SelfTestEntry.cs        Entrada alternativa sin privilegios para la autocomprobación
  app.manifest            requireAdministrator, DPI, longPathAware, supportedOS completos

  App/
    NetForgeContext.cs    Dueño de los servicios; vive sin ventana (ApplicationContext)
    FileShareService.cs   FTP + portal + QR coordinados, reatados al renacer la red
    TrayIcon.cs           Icono de bandeja y avisos
    SelfTest.cs            Más de 230 comprobaciones: lógica pura, servidores reales, radar,
                          identidad visual, ventana real en dos idiomas y caminos de fallo
                          de los motores

  Core/
    CapabilityProbe.cs    Sondeo real de lo que puede hacer ESTE equipo
    MachineProfile.cs     Modelo del sondeo + informe copiable
    HotspotController.cs  Orden de motores, arranque en cadena, reinicio
    DaemonGuard.cs        Vigilancia, resurrección, espera creciente
    IHotspotEngine.cs     Contrato + validación de SSID/clave + localización del adaptador
    InterfaceLocator.cs   Adaptador del punto de acceso y aritmética de subred
    IcsSharing.cs         Uso compartido de conexión (COM tardío) con desmontaje limpio
    AppSettings.cs        Preferencias en texto plano legible
    Autostart.cs          Tarea programada con privilegios altos
    ShellRunner.cs        Ejecución de herramientas del sistema por código de salida

  Core/Engines/
    WiFiDirectGoEngine.cs        Grupo autónomo: el motor que funciona sin internet
    MobileHotspotEngine.cs       API moderna de Windows (y el único que enumera clientes)
    LegacyHostedNetworkEngine.cs Red hospedada clásica (netsh + confirmación por wlanapi)

  Native/
    WinRtAsync.cs         Puente síncrono para WinRT sin el ensamblado unión
    WlanApi.cs            wlanapi.dll: interfaces y estado de la red hospedada
    SystemApi.cs          iphlpapi (ARP), psapi (memoria), ntdll (versión real)

  Net/
    RadarService.cs       Auditoría cada 3 s en hilo de fondo
    OuiDatabase.cs        Fabricantes sin internet (búsqueda binaria en memoria)
    LanClient.cs          Identidad por MAC, con presencia y estado de bloqueo

  Firewall/
    FirewallRuleManager.cs Reglas por COM en proceso, con netsh de respaldo
    FirewallBanList.cs     Lista negra persistida, indultos y reaplicación

  Ftp/, Web/, Qr/          Servidor FTP, portal HTTP y codificador QR propios

  Ui/                      Ventana, tema oscuro, textos ES/EN, registro
    MainForm.cs           Panel de control: cabecera, seis pestañas, atajos y refresco
    Theme.cs              Paleta, tipografía, formas redondeadas, contraste y fábricas
    Widgets.cs            Controles dibujados a mano: botones, casillas, tarjetas, cápsulas,
                          pestañas planas y lista del registro con color por gravedad
    Strings.cs            Tabla de textos en español e inglés
    Log.cs                Registro en memoria y en fichero rotado
```

## Interfaz

La ventana es un panel de control, no una demostración: cada decisión visual responde a algo
que se rompía o no se leía.

- **Controles dibujados a mano**. No hay diseñador de formularios (se compila con `csc`
  directo) y tampoco se usan los controles de serie para lo que tiene carácter propio: botones
  redondeados con estado de ratón, foco y deshabilitado; casillas oscuras; tarjetas con borde
  suave; cápsulas de estado con punto de color; pestañas planas que miden su texto. El
  resultado es el mismo con y sin estilos visuales de Windows, y por eso las capturas de
  revisión (`NETFORGE_UI_DUMP`) sirven para lo que se ve en pantalla: la autocomprobación carga
  los mismos estilos que la aplicación.
- **El contraste se comprueba, no se intuye**. La paleta tiene tres tonos de texto y cuatro
  fondos, y la autocomprobación calcula la relación de contraste (WCAG 2.1) de cada combinación,
  de los botones en todos sus estados y de las cápsulas. El listón es 4.5:1: si alguien oscurece
  un gris "porque queda bien", la comprobación falla antes que la vista del usuario.
- **Nada se sale de la ventana mínima**. Las pestañas reparten el ancho disponible entre las
  seis según su texto, y las filas que pueden quedar estrechas se dejan envolver: con la ventana
  en su tamaño mínimo nada queda recortado.
- **El refresco no molesta**. El radar repinta cada pocos segundos; la lista de equipos se
  actualiza celda a celda cuando los equipos son los mismos (así no se pierde el orden elegido,
  ni el desplazamiento, ni parpadea), y el código QR solo se vuelve a codificar cuando cambia la
  dirección que lleva dentro.

## Selección de motor

`CapabilityProbe` construye un `MachineProfile` con datos **estructurales**: `wlanapi` para enumerar interfaces y preguntar por la red hospedada, la API de radios para el estado de la radio, la API de punto de acceso para la capacidad de tethering, y `RtlGetVersion` para la versión real de Windows (con todos los `supportedOS` declarados en el manifiesto, porque sin ellos Windows miente y responde 6.2).

Nunca se interpreta texto de `netsh`: sus mensajes salen traducidos y cualquier comparación de cadenas se rompería en un Windows en otro idioma. Cuando se usa `netsh` (motor heredado, reglas de firewall de respaldo, tareas programadas) la decisión se toma por **código de salida**, y el estado se confirma después con la API correspondiente.

Orden de intentos según la preferencia:

- **Automático**: si hay internet, primero el punto de acceso moderno (es el que Windows sabe enrutar); si no hay, primero Wi-Fi Direct (que no depende de internet).
- **Off-grid**: Wi-Fi Direct, luego heredado, luego punto de acceso.
- **Compartir**: punto de acceso, luego Wi-Fi Direct, luego heredado.
- **Legacy**: el modo heredado primero.

Si un motor falla al arrancar, se prueba el siguiente y el error de cada uno queda registrado; solo se informa de fallo cuando no queda ninguno. La ventana muestra siempre **qué motor está activo**.

## Daemon Guard

Tres reglas que separan "funciona" de "funciona siempre":

1. **Sin solapamiento**: un tick no puede ejecutarse encima de otro (`Interlocked.CompareExchange`). Si un reinicio tarda más que el intervalo, el siguiente tick se descarta en lugar de lanzar dos arranques a la vez.
2. **El usuario manda**: un "Detener red" desarma la vigilancia. El guard nunca resucita lo que se apagó a propósito.
3. **Sin tormenta de reinicios**: tras cada fallo la espera crece (2, 4, 8, 16, 30 s) y a los 5 fallos consecutivos avisa y deja de insistir en ese ciclo.

Se dispara también con avisos del sistema (reanudación tras suspensión, cambio de dirección o disponibilidad de red), lo que reduce la detección a milisegundos en los casos típicos.

Como la dirección del adaptador puede cambiar al renacer la red, los servidores de archivos se vuelven a abrir sobre la dirección nueva.

## Seguridad

- **Jaula de rutas** en portal y FTP: cada ruta se resuelve con `Path.GetFullPath` y se comprueba que siga dentro de la carpeta compartida. Las rutas absolutas se rechazan explícitamente, no se "interpretan".
- **Solo descarga por defecto**: que alguien escanee un QR no debería poder borrar nada.
- **Subidas en flujo continuo**: el lector multipart escribe sobre disco mientras busca el delimitador, así que un archivo grande nunca se carga en memoria. Un delimitador que aparece a medias dentro del contenido se trata como datos, no como fin de parte.
- **Clave aleatoria**: se genera con el generador criptográfico del sistema en el primer arranque.
- **Sin inyección de línea de comandos**: el SSID y la clave se validan contra comillas y caracteres de control antes de llegar a `netsh`.
- **Escucha acotada**: los servidores de archivos se atan a la dirección del punto de acceso, no a `0.0.0.0`.
- **Uso compartido reversible**: ICS se desactiva siempre al detener la red o cerrar la aplicación.

## Compilación sin Visual Studio

El proyecto se compila con `csc.exe` (Roslyn) descargado de NuGet y con las bibliotecas del propio Windows, incluidas las API modernas a través de la metadata dividida de `%windir%\System32\WinMetadata`. Dos detalles descubiertos durante el desarrollo y documentados en el código:

- `System.Runtime.WindowsRuntime.AsTask()` no se puede usar: su firma referencia el ensamblado unión `Windows.winmd`, que no existe en Windows 10/11. De ahí `WinRtAsync`.
- Los nombres reales de la metadata no siempre coinciden con la documentación (por ejemplo, el resultado del tethering es `NetworkOperatorTetheringOperationResult`, y `ConnectionProfile` no expone `ProfileId`). El sondeo se hace con criterio empírico, no con lo que dice el manual.
