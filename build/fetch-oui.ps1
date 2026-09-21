# SPDX-License-Identifier: GPL-3.0-or-later
# NetForge Studio - genera assets\oui.bin.gz a partir de los registros publicos del IEEE.
# El resultado es un fichero de registros de tamano fijo, ordenado por prefijo, pensado
# para busqueda binaria en memoria: el radar identifica fabricantes sin tocar la red.
[CmdletBinding()]
param(
    [string]$Output = 'assets\oui.bin.gz',
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$target = Join-Path $root $Output

if ((Test-Path $target) -and -not $Force) {
    Write-Host "[oui] ya existe: $target"
    exit 0
}

$tmp = Join-Path $root 'build\oui-tmp'
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null

# MA-L (24 bits), MA-M (28 bits) y MA-S (36 bits). Los tres importan: los prefijos largos
# son cada vez mas frecuentes y sin ellos muchos equipos modernos quedarian sin fabricante.
$sources = @(
    @{ Name = 'ouil'; Bits = 24; Url = 'https://standards-oui.ieee.org/oui/oui.csv' },
    @{ Name = 'ouim'; Bits = 28; Url = 'https://standards-oui.ieee.org/oui28/mam.csv' },
    @{ Name = 'ouis'; Bits = 36; Url = 'https://standards-oui.ieee.org/oui36/oui36.csv' }
)

# El prefijo se guarda como entero de 64 bits con los bits significativos arriba, para
# que los tres registros (24, 28 y 36 bits) quepan en el mismo formato.
$NameSlot = 32
$RecordSize = 8 + $NameSlot
$blocks = @{}

# El sitio del IEEE rechaza peticiones sin cabecera de navegador, asi que se envia una.
$userAgent = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36'

foreach ($source in $sources) {
    $csv = Join-Path $tmp ($source.Name + '.csv')
    if (-not (Test-Path $csv)) {
        Write-Host "[oui] descargando $($source.Url)"
        try {
            Invoke-WebRequest -Uri $source.Url -OutFile $csv -UseBasicParsing -UserAgent $userAgent -TimeoutSec 180
        }
        catch {
            Write-Warning "[oui] no se pudo descargar $($source.Url): $($_.Exception.Message)"
            continue
        }
    }

    $records = New-Object 'System.Collections.Generic.Dictionary[uint64,string]'
    $lines = Get-Content -Path $csv -Encoding UTF8
    foreach ($line in $lines) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $parts = $line.Split(',')
        if ($parts.Count -lt 3) { continue }

        $hex = $parts[1].Trim().Trim('"')
        if ($hex.Length -lt 6) { continue }
        if ($hex -notmatch '^[0-9A-Fa-f]+$') { continue }

        $name = $parts[2].Trim().Trim('"')
        if ([string]::IsNullOrWhiteSpace($name)) { continue }
        if ($name.Length -gt $NameSlot) { $name = $name.Substring(0, $NameSlot) }

        # Un prefijo hexadecimal completo son 12 digitos; se toman los bits que definen el
        # registro (24, 28 o 36) desplazados a la parte alta de un entero de 32 bits.
        $chars = [int]($source.Bits / 4)
        if ($hex.Length -lt $chars) { continue }
        $value = [Convert]::ToUInt64($hex.Substring(0, $chars), 16)
        $shift = 64 - $source.Bits
        $prefix = [uint64]($value -shl $shift)

        if (-not $records.ContainsKey($prefix)) {
            $records[$prefix] = $name
        }
    }

    $blocks[$source.Bits] = $records
    Write-Host "[oui] $($source.Name): $($records.Count) prefijos de $($source.Bits) bits"
}

if (-not $blocks.ContainsKey(24) -or $blocks[24].Count -eq 0) {
    throw '[oui] sin el registro principal (MA-L) no tiene sentido generar la base de datos'
}

# Formato binario: cabecera de 20 bytes (magic, version, relleno, tres contadores) y tres
# bloques de registros de 40 bytes (prefijo uint64 + nombre ASCII de 32, relleno de ceros).
$stream = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($stream)

$writer.Write([System.Text.Encoding]::ASCII.GetBytes('NOUI'))
$writer.Write([byte]1)
$writer.Write([byte]0)
$writer.Write([byte]0)
$writer.Write([byte]0)

foreach ($bits in 24, 28, 36) {
    $writer.Write([int]$blocks[$bits].Count)
}

foreach ($bits in 24, 28, 36) {
    $sorted = $blocks[$bits].Keys | Sort-Object
    foreach ($key in $sorted) {
        $writer.Write([uint64]$key)
        $nameBytes = [System.Text.Encoding]::ASCII.GetBytes($blocks[$bits][$key])
        $slot = New-Object byte[] $NameSlot
        [Array]::Copy($nameBytes, $slot, [Math]::Min($nameBytes.Length, $NameSlot))
        $writer.Write($slot)
    }
}

$writer.Flush()
$payload = $stream.ToArray()
$writer.Dispose()
$stream.Dispose()

$file = [System.IO.File]::Create($target)
$gzip = New-Object System.IO.Compression.GZipStream($file, [System.IO.Compression.CompressionLevel]::Optimal)
$gzip.Write($payload, 0, $payload.Length)
$gzip.Dispose()
$file.Dispose()

Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
Write-Host ("[oui] escrito {0} ({1} KB sin comprimir -> {2} KB en disco)" -f $target, [int]($payload.Length / 1024), [int]((Get-Item $target).Length / 1024))
