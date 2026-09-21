# SPDX-License-Identifier: GPL-3.0-or-later
# NetForge Studio - paquete portable.
#
# Genera dist\NetForge-Studio-<version>-portable.zip con el ejecutable, la licencia, el
# historial, la documentacion y las notas de seguridad. La promesa del proyecto es "un solo
# ejecutable, sin instalador, sin dependencias": el paquete lo respeta y no anade nada mas.
#
# Al final se abre el ZIP y se comprueba lo que contiene de verdad, no lo que se pretendia
# meter: un paquete al que le falta la GPL no se publica.
[CmdletBinding()]
param(
    [string]$Version = '',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
Set-Location $root

if (-not $Version) {
    # La version sale del historial de cambios: una sola fuente de verdad.
    $changelog = Join-Path $root 'CHANGELOG.md'
    if (-not (Test-Path $changelog)) {
        throw '[paquete] falta CHANGELOG.md, del que se deduce la version'
    }

    $match = Select-String -Path $changelog -Pattern '^##\s*\[([0-9]+\.[0-9]+\.[0-9]+)\]' | Select-Object -First 1
    if (-not $match) {
        throw '[paquete] no se encontro ninguna version en CHANGELOG.md'
    }

    $Version = $match.Matches[0].Groups[1].Value
}

Write-Host "[paquete] version $Version"

if (-not $SkipBuild) {
    & (Join-Path $root 'build.ps1')
}

$exe = Join-Path $root 'bin\NetForge.exe'
if (-not (Test-Path $exe)) {
    throw "[paquete] no existe ${exe}: compila antes de empaquetar"
}

$dist = Join-Path $root 'dist'
if (Test-Path $dist) {
    Remove-Item -Recurse -Force $dist
}

$staging = Join-Path $dist 'NetForge-Studio'
New-Item -ItemType Directory -Path $staging -Force | Out-Null
$docsFolder = Join-Path $staging 'docs'
New-Item -ItemType Directory -Path $docsFolder -Force | Out-Null

# Lo que viaja en el paquete, y solo eso.
$files = @(
    @{ From = $exe;                                  To = 'NetForge.exe' },
    @{ From = (Join-Path $root 'LICENSE');           To = 'LICENSE' },
    @{ From = (Join-Path $root 'README.md');         To = 'LEEME.md' },
    @{ From = (Join-Path $root 'CHANGELOG.md');      To = 'CHANGELOG.md' },
    @{ From = (Join-Path $root 'SECURITY.md');       To = 'SECURITY.md' },
    @{ From = (Join-Path $root 'docs\limites-conocidos.md'); To = 'docs\limites-conocidos.md' },
    @{ From = (Join-Path $root 'docs\compatibilidad.md');    To = 'docs\compatibilidad.md' }
)

foreach ($file in $files) {
    if (-not (Test-Path $file.From)) {
        throw "[paquete] falta un fichero obligatorio: $($file.From)"
    }

    $target = Join-Path $staging $file.To
    $targetFolder = Split-Path -Parent $target
    if (-not (Test-Path $targetFolder)) {
        New-Item -ItemType Directory -Path $targetFolder -Force | Out-Null
    }

    Copy-Item -Path $file.From -Destination $target -Force
}

# Sin esto, el usuario que abre el ZIP en Windows ejecuta un binario bajado de internet sin
# contexto: aqui se explica que es, que pide administrador y por que.
$readme = @"
NetForge Studio $Version - paquete portable
==========================================

Este programa convierte este PC en un router Wi-Fi local y reparte archivos por esa red.
No necesita internet, ni instalador, ni dependencias: NetForge.exe es todo.

Como usarlo
-----------
1. Copia la carpeta donde quieras y ejecuta NetForge.exe.
2. Windows pedira permisos de administrador UNA vez: son necesarios para crear la red y
   tocar el firewall. Sin ellos no hay nada que hacer, y no se usa ningun truco para
   saltarse el aviso.
3. Escribe el nombre de la red y la clave, pulsa "Crear red".
4. En la pestana Archivos, elige la carpeta y pulsa "Iniciar servidores".
5. Escanea el codigo QR con el movil, o usa "Copiar enlace".

Para apagarlo del todo, usa "Salir del todo" en el icono de la bandeja: avisa antes, porque
cierra la red y los servidores de archivos.

Licencia
--------
GNU GPL v3 o posterior: ver LICENSE. El texto completo en ingles esta en el ejecutable y en
este paquete; LEEME.md es la traduccion del README del repositorio.

Antes de preocuparte por algo, lee docs\limites-conocidos.md: dice sin adornos lo que este
programa NO puede hacer (por ejemplo, el firewall de Windows no filtra por direccion MAC).
"@
Set-Content -Path (Join-Path $staging 'LEEME.txt') -Value $readme -Encoding UTF8

$zip = Join-Path $dist ("NetForge-Studio-$Version-portable.zip")

# Las entradas se escriben a mano, con barras normales. Compress-Archive de PowerShell 5.1
# guarda las rutas con barra invertida, y entonces el ZIP se ve como un unico fichero de
# nombre raro en Linux, en macOS y en la mitad de las aplicaciones de movil: no es un
# paquete portable, es un fichero roto que solo entiende el Explorador de Windows.
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$entries = New-Object System.Collections.Generic.List[object]
foreach ($file in $files) {
    $entries.Add(@{ Path = (Join-Path $staging $file.To); Name = 'NetForge-Studio/' + ($file.To -replace '\\', '/') })
}

$entries.Add(@{ Path = (Join-Path $staging 'LEEME.txt'); Name = 'NetForge-Studio/LEEME.txt' })

$archive = [System.IO.Compression.ZipFile]::Open($zip, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($entry in $entries) {
        $item = $archive.CreateEntry($entry.Name, [System.IO.Compression.CompressionLevel]::Optimal)
        $target = $item.Open()
        try {
            $source = [System.IO.File]::OpenRead($entry.Path)
            try {
                $source.CopyTo($target)
            }
            finally {
                $source.Dispose()
            }
        }
        finally {
            $target.Dispose()
        }
    }
}
finally {
    $archive.Dispose()
}

# Comprobacion del resultado: se abre el ZIP y se mira lo que hay dentro de verdad.
$archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
try {
    $names = @($archive.Entries | ForEach-Object { $_.FullName })
    $required = @(
        'NetForge-Studio/NetForge.exe',
        'NetForge-Studio/LICENSE',
        'NetForge-Studio/LEEME.md',
        'NetForge-Studio/LEEME.txt',
        'NetForge-Studio/CHANGELOG.md',
        'NetForge-Studio/docs/limites-conocidos.md'
    )

    foreach ($name in $required) {
        if ($names -notcontains $name) {
            throw "[paquete] el ZIP no contiene $name"
        }
    }

    $executable = $archive.Entries | Where-Object { $_.FullName -replace '\\', '/' -eq 'NetForge-Studio/NetForge.exe' } | Select-Object -First 1
    if ($executable.Length -lt 100000) {
        throw "[paquete] el ejecutable del paquete pesa $($executable.Length) bytes: sospechosamente poco"
    }

    $unexpected = $names | Where-Object { $_ -match '\.(pdb|dll|config|log)$' }
    if ($unexpected) {
        throw "[paquete] el paquete lleva ficheros que no deberia: $($unexpected -join ', ')"
    }

    # Rutas con barra invertida: el ZIP solo lo abre Windows. Se comprueba aqui para no
    # descubrirlo cuando alguien lo intente abrir desde un telefono.
    $backslashes = $names | Where-Object { $_ -match '\\' }
    if ($backslashes) {
        throw "[paquete] el ZIP guarda rutas con barra invertida: $($backslashes -join ', ')"
    }

    Write-Host "[paquete] comprobado: $($names.Count) entradas, ejecutable de $([math]::Round($executable.Length / 1KB)) KB"
}
finally {
    $archive.Dispose()
}

# Ultima prueba: descomprimir como lo haria un tercero y mirar que el ejecutable sigue entero.
$probe = Join-Path $dist 'comprobacion'
[System.IO.Compression.ZipFile]::ExtractToDirectory($zip, $probe)
try {
    $extracted = Join-Path $probe 'NetForge-Studio\NetForge.exe'
    if (-not (Test-Path $extracted)) {
        throw '[paquete] al descomprimir no aparece el ejecutable'
    }

    $original = (Get-Item $exe).Length
    $copy = (Get-Item $extracted).Length
    if ($original -ne $copy) {
        throw "[paquete] el ejecutable del paquete no coincide con el compilado ($copy bytes frente a $original)"
    }
}
finally {
    Remove-Item -Recurse -Force $probe
}

$size = [math]::Round((Get-Item $zip).Length / 1KB)
Write-Host "[paquete] listo: dist\NetForge-Studio-$Version-portable.zip ($size KB)"
