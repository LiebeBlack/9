# SPDX-License-Identifier: GPL-3.0-or-later
# NetForge Studio - descarga ZXing.Net SOLO para las comprobaciones.
#
# No es una dependencia del producto: el ejecutable que se distribuye no referencia esta
# biblioteca. Se usa como lector independiente para verificar que los codigos QR que genera
# NetForge se pueden leer de verdad.
[CmdletBinding()]
param([string]$Version = '0.16.9')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$target = Join-Path $root 'build\tools\zxing.dll'

if (Test-Path $target) {
    Write-Host "[zxing] ya presente: $target"
    exit 0
}

$zip = Join-Path $root 'build\tools\zxing.zip'
$tmp = Join-Path $root 'build\tools\_zxing'
$url = "https://api.nuget.org/v3-flatcontainer/zxing.net/$Version/zxing.net.$Version.nupkg"

Write-Host "[zxing] descargando $url"
Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing -TimeoutSec 180
if (Test-Path $tmp) {
    Remove-Item $tmp -Recurse -Force
}

Expand-Archive -Path $zip -DestinationPath $tmp -Force
$library = Join-Path $tmp 'lib\net48\zxing.dll'
if (-not (Test-Path $library)) {
    throw '[zxing] el paquete no trae la version para .NET Framework 4.8'
}

Copy-Item $library $target -Force
Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $zip -Force -ErrorAction SilentlyContinue
Write-Host "[zxing] listo: $target"
