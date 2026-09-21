# SPDX-License-Identifier: GPL-3.0-or-later
# NetForge Studio - comprueba el codigo QR con un lector independiente.
#
# Codifica varios textos (URLs del portal, acentos, textos largos) con los cuatro niveles de
# correccion, los convierte en imagen y los vuelve a leer con ZXing. Si ZXing devuelve
# exactamente el texto original, la informacion de formato, la mascara, la version, la
# colocacion de modulos y el Reed-Solomon son correctos.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

& (Join-Path $root 'build\fetch-toolchain.ps1') | Out-Null
& (Join-Path $root 'build\fetch-zxing.ps1')

$csc = Join-Path $root 'build\tools\csc.exe'
$zxing = Join-Path $root 'build\tools\zxing.dll'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path $framework)) {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}

$bin = Join-Path $root 'bin'
New-Item -ItemType Directory -Force -Path $bin | Out-Null
$output = Join-Path $bin 'NetForge.QrCheck.exe'

$arguments = @(
    '-nologo',
    '-target:exe',
    '-langversion:7.3',
    "-out:$output",
    "-r:$(Join-Path $framework 'System.dll')",
    "-r:$(Join-Path $framework 'System.Core.dll')",
    "-r:$(Join-Path $framework 'System.Drawing.dll')",
    "-r:$zxing"
)

$sources = @()
$sources += (Join-Path $root 'src\Qr\QrEncoder.cs')
$sources += (Join-Path $root 'src\Qr\QrRenderer.cs')
$sources += (Join-Path $root 'build\check-qr\CheckQr.cs')

Write-Host '[qr] compilando la comprobacion'
& $csc ($arguments + $sources)
if ($LASTEXITCODE -ne 0) {
    throw '[qr] no se pudo compilar la comprobacion'
}

Copy-Item $zxing (Join-Path $bin 'zxing.dll') -Force
Write-Host '[qr] ejecutando'
& $output
if ($LASTEXITCODE -ne 0) {
    throw '[qr] la comprobacion del codigo QR fallo'
}
