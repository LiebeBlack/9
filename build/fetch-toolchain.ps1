# SPDX-License-Identifier: GPL-3.0-or-later
# NetForge Studio - obtiene un compilador Roslyn autonomo (sin Visual Studio, sin admin).
[CmdletBinding()]
param(
    [string[]]$Versions = @('4.2.0', '3.11.0', '3.7.0'),
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$tools = Join-Path $root 'build\tools'
$csc = Join-Path $tools 'csc.exe'

if ((Test-Path $csc) -and -not $Force) {
    Write-Host "[toolchain] ya presente: $csc"
    & $csc /nologo /version
    exit 0
}

New-Item -ItemType Directory -Force -Path $tools | Out-Null

foreach ($v in $Versions) {
    $url = "https://api.nuget.org/v3-flatcontainer/microsoft.net.compilers/$v/microsoft.net.compilers.$v.nupkg"
    $zip = Join-Path $tools "_pkg-$v.zip"
    $tmp = Join-Path $tools "_x-$v"
    Write-Host "[toolchain] descargando $url"
    try {
        Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing -TimeoutSec 120
    }
    catch {
        Write-Warning "[toolchain] no se pudo descargar $v : $($_.Exception.Message)"
        continue
    }

    if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
    try {
        Expand-Archive -Path $zip -DestinationPath $tmp -Force
    }
    catch {
        Write-Warning "[toolchain] paquete $v ilegible: $($_.Exception.Message)"
        Remove-Item $zip -Force -ErrorAction SilentlyContinue
        continue
    }

    # La version de escritorio trae tools\csc.exe sobre .NET Framework; las
    # versiones nuevas del Toolset solo traen csc.dll y exigen dotnet runtime.
    $found = Join-Path $tmp 'tools\csc.exe'
    if (-not (Test-Path $found)) {
        Write-Warning "[toolchain] $v no incluye tools\csc.exe (solo compilador para dotnet)"
        Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item $zip -Force -ErrorAction SilentlyContinue
        continue
    }

    if (Test-Path $tools) {
        Get-ChildItem $tools -Exclude '_pkg-*.zip', '_x-*' | ForEach-Object {
            Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    Copy-Item -Path (Join-Path $tmp 'tools\*') -Destination $tools -Recurse -Force
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $zip -Force -ErrorAction SilentlyContinue
    Write-Host "[toolchain] instalado Roslyn $v en build\tools"
    & $csc /nologo /version
    exit 0
}

throw "[toolchain] ninguna version disponible aporta tools\csc.exe. Sin compilador no hay build."
