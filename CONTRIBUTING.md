# Cómo contribuir

Gracias por el interés. Este proyecto tiene reglas poco habituales y conviene conocerlas antes de escribir código.

## Reglas del proyecto

1. **Cero dependencias en tiempo de ejecución.** El ejecutable que se distribuye no referencia ninguna biblioteca de terceros. Si necesitas algo (un codificador, un servidor), se implementa o se justifica muy bien por qué no.
2. **Nada de texto localizado.** Nunca se decide en función de lo que imprime una herramienta del sistema: `netsh`, `schtasks` y compañía responden en el idioma de Windows. Se decide por **código de salida** y se confirma el estado con la API correspondiente.
3. **Nada de suposiciones sobre el hardware.** No hay rutas, nombres de adaptador ni direcciones fijas. Se sondea en runtime y se explica al usuario qué se encontró.
4. **Cualquier cambio de comportamiento lleva su comprobación** en `src/App/SelfTest.cs`. La autocomprobación se ejecuta sin permisos de administrador y tiene que seguir pasando.
5. **Nada de trucos para saltarse el UAC.** La elevación se declara en el manifiesto y punto.

## Preparar el entorno

No hace falta Visual Studio ni el SDK de .NET: el compilador se descarga solo.

```powershell
./build.ps1              # compila y ejecuta la autocomprobación
./scripts/check-qr.ps1   # verifica el código QR con un lector independiente
./scripts/check-site.ps1 # comprueba el sitio web (traducciones y dependencias)
./scripts/package.ps1    # compila y empaqueta la versión portable verficada
```

## Antes de enviar un cambio

- [ ] `./build.ps1` termina sin avisos (se compila con avisos como errores).
- [ ] `./bin/NetForge.Autotest.exe` pasa todas las comprobaciones.
- [ ] Si el cambio toca el código QR, `./scripts/check-qr.ps1` sigue leyendo los códigos generados.
- [ ] Si el cambio toca el sitio, `./scripts/check-site.ps1` sigue pasando (los 219 pares de texto tienen que seguir pareados).
- [ ] Si el cambio toca la red, se ha probado en un equipo real y se describe en la descripción del cambio qué motor se usó y con qué adaptador.
- [ ] Los archivos nuevos llevan la cabecera `SPDX-License-Identifier: GPL-3.0-or-later`.
- [ ] Los textos visibles están en los dos idiomas de `src/Ui/Strings.cs` (español e inglés), y los del sitio en sus atributos `data-es` y `data-en`.

## El sitio del proyecto

`site/index.html` es una sola página con todo dentro: no se compila, no usa CDN, ni fuentes externas, ni un generador. Se edita el HTML y ya está. Dos detalles a tener en cuenta:

- **Los enlaces al repositorio y a la descarga están en dos constantes al principio del script** (`REPO` y `RELEASE`). Al publicar un clon, se cambian ahí y solo ahí: el resto de la página usa esas variables.
- **Cada texto visible lleva `data-es` y `data-en`** (o `data-es-html` y `data-en-html` si lleva formato dentro). `scripts/check-site.ps1` rechaza cualquier etiqueta a la que le falte uno de los dos.

## Informes de error

Usa la plantilla de informe: pide el **informe de diagnóstico** de la aplicación, que es lo que permite distinguir un fallo del programa de una limitación del hardware. Si el problema es "no levanta la red", ese informe es imprescindible.

## Estilo

- Comentarios y nombres en español, claros y sin adornos. Los comentarios explican **por qué**, no qué hace la línea.
- El código que rodea a las API de Windows debe llevar comentario cuando el motivo no es evidente (por ejemplo, por qué se evita `AsTask` o por qué se usa un orden de bytes concreto). Varios de esos detalles se descubrieron a base de golpes y no quiero que se pierdan.
