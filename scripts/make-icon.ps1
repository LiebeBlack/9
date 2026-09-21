# SPDX-License-Identifier: GPL-3.0-or-later
# NetForge Studio - dibuja el icono de la aplicacion.
# Se genera por codigo en vez de guardar un binario opaco: cualquiera puede cambiarlo.
[CmdletBinding()]
param([string]$Output = 'assets\netforge.ico', [switch]$Force)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$target = Join-Path $root $Output

if ((Test-Path $target) -and -not $Force) {
    Write-Host "[icon] ya existe: $target"
    exit 0
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
Add-Type -AssemblyName System.Drawing

$size = 64
$bitmap = New-Object System.Drawing.Bitmap($size, $size)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::FromArgb(13, 17, 23))

# Tres arcos concentricos: la senal de la red local.
$accent = [System.Drawing.Color]::FromArgb(88, 166, 255)
$centerX = $size / 2
$centerY = $size - 6
foreach ($radius in 14, 26, 38) {
    $pen = New-Object System.Drawing.Pen($accent, [float](6 - ($radius / 14)))
    $rect = New-Object System.Drawing.RectangleF(($centerX - $radius), ($centerY - $radius), ($radius * 2), ($radius * 2))
    $graphics.DrawArc($pen, $rect, 215, 110)
    $pen.Dispose()
}

# Punto de emision.
$dot = New-Object System.Drawing.SolidBrush($accent)
$graphics.FillEllipse($dot, ($centerX - 5), ($centerY - 5), 10, 10)
$dot.Dispose()
$graphics.Dispose()

$handle = $bitmap.GetHicon()
$icon = [System.Drawing.Icon]::FromHandle($handle)
$stream = [System.IO.File]::Create($target)
$icon.Save($stream)
$stream.Dispose()
$icon.Dispose()
$bitmap.Dispose()

Write-Host ("[icon] escrito {0}" -f $target)
