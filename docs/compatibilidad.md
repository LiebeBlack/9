# Compatibilidad

La aplicación no lleva una lista de adaptadores compatibles: **sondea cada equipo al arrancar** y elige motor. Esta tabla explica qué se sondea y qué implica cada resultado. El panel **Diagnóstico** de la aplicación muestra estos mismos datos para tu máquina, listos para copiar en un informe de error.

## Lo que se sondea

| Capacidad | Cómo se comprueba | Consecuencia |
|---|---|---|
| Versión de Windows | `RtlGetVersion` (no `Environment.OSVersion`, que miente) | Por debajo de 10.0.18362 avisa; sigue funcionando sin garantías |
| Interfaces Wi-Fi | `WlanEnumInterfaces` de `wlanapi` | Sin ninguna, no hay red inalámbrica posible |
| Radio Wi-Fi | API de radios de Windows | Si está apagada, se avisa antes de intentar nada |
| Wi-Fi Direct (grupo) | Construcción de `WiFiDirectAdvertisementPublisher` | Habilita el motor que funciona **sin internet** |
| Red hospedada (Soft AP) | `WlanHostedNetworkQueryStatus` y `WlanHostedNetworkQueryProperty` | Habilita el motor heredado (y confirma que Windows repartirá direcciones solo si se comparte conexión) |
| Punto de acceso moderno | Creación del gestor de tethering desde un perfil guardado | Habilita compartir internet de forma nativa y la enumeración exacta de clientes |
| Servicio de uso compartido | Servicio `SharedAccess` | Necesario para repartir internet |
| Perfiles de conexión | `GetConnectionProfiles` | Sin ningún perfil guardado, el motor moderno no puede crearse (Wi-Fi Direct sí) |
| Elevación | Pertenencia del proceso a Administradores | Sin ella, se avisa en la barra superior |

## Qué motor acaba usándose

| Escenario | Motor | Resultado |
|---|---|---|
| Wi-Fi Direct disponible (lo normal desde Windows 10) | Grupo autónomo | SSID visible para cualquier móvil, direcciones repartidas por el grupo, funciona sin internet |
| Driver con Soft AP y hay internet | Punto de acceso moderno | SSID + reparto de internet nativo + lista de clientes confirmada por el sistema |
| Solo red hospedada (Windows antiguos) | Modo heredado | SSID visible; los clientes necesitan que se active compartir conexión para obtener dirección |
| Sin ninguna capacidad | — | La aplicación lo dice con el motivo concreto de cada motor, sin fallar en silencio |

## Casos reales comprobados

| Equipo | Resultado del sondeo | Motor elegido |
|---|---|---|
| Portátil con Intel Wireless-AC 9560 en Windows 11 (build 29671) | Red hospedada **no**, Soft AP **no**, Wi-Fi Direct **sí**, radio encendida | Wi-Fi Direct (grupo autónomo). Es el caso que motivó que el motor heredado no fuera el principal |

En este mismo equipo el modo "compartir internet" puede no estar disponible, porque el API de uso compartido no acepta adaptadores virtuales de Wi-Fi Direct: la aplicación lo intenta, y si el sistema lo rechaza lo informa con claridad.

## Cómo ampliar la tabla

Si tu equipo aparece en la lista de "no compatible" en algún apartado, abre un informe de error con el contenido del panel **Diagnóstico**: incluye versión real de Windows, adaptador, capacidades sondeadas e interfaces con sus direcciones. Con ese informe se puede saber si falta un motor o si el hardware simplemente no puede.
