# Límites conocidos

Este documento existe porque prometer de más es la forma más rápida de decepcionar. Todo lo que sigue está comprobado en el código o medido en una máquina real, y se muestra también dentro de la aplicación para que nadie se lleve una sorpresa.

## 1. El Firewall de Windows no filtra por MAC

El especificado pide "bloquear la dirección MAC/IP del intruso". El firewall de Windows (motor WFP) filtra por **IP, puerto, protocolo y aplicación**, nunca por dirección MAC. No es una limitación de esta aplicación: no existe en el sistema operativo.

Cómo se resuelve: el baneo se **identifica por MAC** (lo único estable, porque la IP cambia al renovar la concesión) y se **aplica por IP**, con una regla de bloqueo entrante y saliente. Un radar reescribe la regla cuando detecta que el dispositivo bloqueado aparece con otra dirección, así que un reinicio del móvil no sirve para escapar. Las reglas de bloqueo tienen preferencia sobre las de permiso en el motor WFP, de modo que no hace falta ninguna "prioridad máxima" artificial.

**Lo que no se puede bloquear así**: el tráfico *entre dos clientes* del mismo punto de acceso. Las tramas van por la capa 2 y nunca pasan por la pila IP del host, por lo que el firewall del PC no las ve. Aislar de verdad a un cliente de los demás exige cuarentena ARP (responder con direcciones falsas para cortarle el paso), una técnica agresiva y frágil que se descartó en el diseño.

## 2. No existen reservas DHCP

Tampoco es una limitación de la aplicación: ni el servicio de uso compartido de Windows (ICS) ni el servidor DHCP del grupo Wi-Fi Direct exponen una API de reservas. La opción elegida ("fijar la concesión") se implementa como baneo por MAC reaplicado en cada pasada del radar, no como reserva real de la dirección.

## 3. El modo "compartir internet" depende del motor y del driver

Para repartir internet hace falta el motor de punto de acceso moderno. Si el driver no implementa Soft AP (es el caso de algunos Intel muy extendidos), Windows puede seguir levantando el grupo Wi-Fi Direct, pero el API pública de uso compartido puede no aceptar ese adaptador virtual como conexión privada. La aplicación lo intenta, y si el sistema lo rechaza lo dice con un mensaje claro en lugar de fallar en silencio.

## 4. "Menos de 10 MB de RAM" no es alcanzable en .NET Framework

Una aplicación de .NET Framework sobre WinForms tiene un suelo práctico de 12 a 25 MB en memoria privada, incluso con la interfaz destruida: el propio runtime pesa eso antes de ejecutar una sola línea.

Qué hace la aplicación en su lugar: el Modo Fantasma destruye la ventana, compacta el montón, devuelve las páginas al sistema con `EmptyWorkingSet` y **muestra el consumo real** en la pestaña de diagnóstico, para que el número que se vea sea el verdadero y no una promesa.

## 5. Los móviles modernos aleatorizan su MAC

Android e iOS cambian la MAC por privacidad. Cuando el bit de administración local está activo, el fabricante **no se puede deducir de ninguna base de datos del mundo**, porque el prefijo es inventado. La aplicación lo detecta y escribe "MAC aleatoria (privacidad)" en lugar de atribuir un fabricante falso. Es un dato correcto, no un fallo.

## 6. El modo heredado no es verificable aquí

El motor de red hospedada clásica (`netsh wlan set hostednetwork`) no puede probarse en el equipo donde se desarrolló, porque su adaptador declara explícitamente `Red hospedada admitida: no`. Está implementado, se activa por orden de preferencia si el sondeo lo considera disponible, y su estado se confirma leyendo la red hospedada desde `wlanapi` (nunca interpretando el texto de `netsh`, que sale traducido en cada Windows).

Además, en ese modo Windows no reparte direcciones por sí sola: hasta que no se activa el uso compartido de conexión, los clientes se quedan sin dirección útil. La aplicación lo advierte en pantalla.

## 7. Crear la red puede cortar tu Wi-Fi actual

Con una sola tarjeta inalámbrica, muchos drivers no permiten ser punto de acceso y cliente a la vez. Al crear la red, la conexión Wi-Fi que tuvieras puede caerse. Se recupera al detener la red.

## 8. El portal web escucha solo en la red local

El servidor de archivos se ata a la dirección del adaptador del punto de acceso, no a todas las interfaces: no queda expuesto por Ethernet, por una VPN ni por otros adaptadores del equipo. Por diseño, tampoco hay acceso desde fuera de la red local.

## 9. Qué no puede verificar la autocomprobación

Merece decirlo con la misma claridad que el resto: la autocomprobación (195 comprobaciones) **no** levanta una red real, ni toca el firewall, ni activa el uso compartido de conexión, porque todo eso exige permisos de administrador y hardware. Lo que sí se ejecuta de verdad, sin privilegios y sin tocar el sistema, es el servidor FTP y el portal web en `127.0.0.1`, el radar con proveedores inyectados, el pintado de la ventana real y los caminos de fallo de los motores (arranque a medias y parada no confirmada).

Es decir: los tres motores de red, el Martillo y el reparto de internet están implementados y se activan por orden de preferencia, pero su primer contacto con un equipo nuevo lo hace el usuario. Si algo falla, el panel de diagnóstico y el registro dicen exactamente qué respondió el sistema.

## 10. Windows anteriores a 10 1903

La aplicación arranca y avisa, pero el motor moderno de punto de acceso no existe ahí. Todo lo que se pueda hacer recae en el modo heredado, sin garantías.
