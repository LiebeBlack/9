# SPDX-License-Identifier: GPL-3.0-or-later
# NetForge Studio - compilacion completa.
#
# Un solo punto de entrada, usado igual en local y en GitHub Actions: obtiene el compilador,
# genera los datos que se pueden regenerar, compila con avisos como errores y ejecuta la
# autocomprobacion. No necesita Visual Studio, ni .NET SDK, ni permisos de administrador.
[CmdletBinding()]
param(
    [switch]$SkipTests,
    [switch]$AllowWarnings,
    [switch]$SkipData,

    # Carpeta de salida. Existe por un caso muy real: si tienes la aplicacion abierta, Windows
    # bloquea bin\NetForge.exe y la compilacion no puede sobrescribirlo. Con -OutDir se compila
    # al lado sin tocar la copia que estas usando.
    [string]$OutDir = 'bin'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSCommandPath
Set-Location $root

Write-Host '[build] obteniendo compilador'
& (Join-Path $root 'build\fetch-toolchain.ps1') | Out-Null
$csc = Join-Path $root 'build\tools\csc.exe'
if (-not (Test-Path $csc)) {
    throw '[build] no hay compilador en build\tools'
}

if (-not $SkipData) {
    & (Join-Path $root 'build\fetch-oui.ps1')
    & (Join-Path $root 'scripts\make-icon.ps1')
}

# Ensamblados del propio Windows: nada de paquetes externos en tiempo de ejecucion.
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path $framework)) {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}

$metadata = Join-Path $env:WINDIR 'System32\WinMetadata'
$references = @(
    (Join-Path $framework 'System.dll'),
    (Join-Path $framework 'System.Core.dll'),
    (Join-Path $framework 'System.Drawing.dll'),
    (Join-Path $framework 'System.Windows.Forms.dll'),
    (Join-Path $framework 'System.ServiceProcess.dll'),
    (Join-Path $framework 'Microsoft.CSharp.dll'),
    (Join-Path $framework 'System.Runtime.dll'),
    (Join-Path $framework 'System.Runtime.WindowsRuntime.dll'),
    (Join-Path $framework 'System.Runtime.InteropServices.WindowsRuntime.dll'),
    (Join-Path $metadata 'Windows.Foundation.winmd'),
    (Join-Path $metadata 'Windows.Devices.winmd'),
    (Join-Path $metadata 'Windows.Networking.winmd'),
    (Join-Path $metadata 'Windows.Storage.winmd'),
    (Join-Path $metadata 'Windows.Security.winmd')
)

foreach ($reference in $references) {
    if (-not (Test-Path $reference)) {
        throw "[build] falta una referencia obligatoria: $reference"
    }
}

$binFolder = Join-Path $root $OutDir
New-Item -ItemType Directory -Force -Path $binFolder | Out-Null
$output = Join-Path $binFolder 'NetForge.exe'
$testOutput = Join-Path $binFolder 'NetForge.Autotest.exe'
$icon = Join-Path $root 'assets\netforge.ico'
$oui = Join-Path $root 'assets\oui.bin.gz'

$sources = @()
foreach ($source in Get-ChildItem -Path (Join-Path $root 'src') -Recurse -Filter *.cs) {
    $sources += $source.FullName
}

$common = New-Object System.Collections.Generic.List[string]
$common.Add('-nologo')
$common.Add('-platform:anycpu')
$common.Add('-langversion:7.3')
$common.Add('-optimize+')
if (-not $AllowWarnings) {
    $common.Add('-warnaserror+')
}

foreach ($reference in $references) {
    $common.Add("-r:$reference")
}

Write-Host ("[build] compilando {0} archivos" -f $sources.Count)

# 1) Aplicacion de ventana, con manifiesto de administrador e icono.
$appArguments = New-Object System.Collections.Generic.List[string]
$appArguments.AddRange($common)
$appArguments.Add('-target:winexe')
$appArguments.Add('-main:NetForge.Program')
$appArguments.Add("-out:$output")
$appArguments.Add(('-win32manifest:' + (Join-Path $root 'src\app.manifest')))
if (Test-Path $icon) {
    $appArguments.Add("-win32icon:$icon")
}

if (Test-Path $oui) {
    $appArguments.Add("-resource:$oui,NetForge.Oui.bin.gz")
}

foreach ($source in $sources) {
    $appArguments.Add($source)
}

& $csc $appArguments.ToArray()
if ($LASTEXITCODE -ne 0) {
    throw "[build] la compilacion fallo con codigo $LASTEXITCODE"
}

$size = [int]((Get-Item $output).Length / 1024)
Write-Host "[build] listo: $output ($size KB)"

# 2) Autocomprobacion: mismo codigo, sin manifiesto de administrador, para que pueda
#    ejecutarse sin UAC (local y en integracion continua).
$testArguments = New-Object System.Collections.Generic.List[string]
$testArguments.AddRange($common)
$testArguments.Add('-target:exe')
$testArguments.Add('-main:NetForge.SelfTestEntry')
$testArguments.Add("-out:$testOutput")
if (Test-Path $oui) {
    $testArguments.Add("-resource:$oui,NetForge.Oui.bin.gz")
}

foreach ($source in $sources) {
    $testArguments.Add($source)
}

& $csc $testArguments.ToArray()
if ($LASTEXITCODE -ne 0) {
    throw "[build] la compilacion del autotest fallo con codigo $LASTEXITCODE"
}

Write-Host "[build] listo: $testOutput"

if (-not $SkipTests) {
    Write-Host '[build] autocomprobacion'
    & $testOutput
    $testCode = $LASTEXITCODE
    if ($testCode -ne 0) {
        throw "[build] la autocomprobacion fallo con codigo $testCode"
    }
}
