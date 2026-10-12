# SPDX-License-Identifier: GPL-3.0-or-later
# NetForge Studio - compilacion principal.
# No requiere Visual Studio, .NET SDK ni permisos de administrador: obtiene un compilador
# Roslyn autonomo y compila con las bibliotecas del propio Windows.
[CmdletBinding()]
param(
    [switch]$SkipData,
    [switch]$SkipSelfTest
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

& (Join-Path $root 'build\fetch-toolchain.ps1') | Out-Null

$csc = Join-Path $root 'build\tools\csc.exe'
if (-not (Test-Path $csc)) { throw '[build] no hay compilador Roslyn en build\tools' }

$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path $framework)) { $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319' }
if (-not (Test-Path $framework)) { throw '[build] no se encontro la runtime de .NET Framework' }

$bin = Join-Path $root 'bin'
New-Item -ItemType Directory -Force -Path $bin | Out-Null

# Recursos. La base OUI viaja incrustada en el ejecutable como recurso de nombre NetForge.Oui.bin.gz.
$embed = @()
if (-not $SkipData) {
    $oui = Join-Path $root 'assets\oui.bin.gz'
    if (Test-Path $oui) {
        $embed += '-resource:' + $oui + ';NetForge.Oui.bin.gz'
        Write-Host "[build] incrustando base OUI: $oui (" + [math]::Round((Get-Item $oui).Length / 1KB) + " KB)"
    } else {
        Write-Warning "[build] assets\oui.bin.gz no esta presente: el radar no identificara fabricantes"
    }

    $icow = Join-Path $root 'assets\netforge.ico'
    if (Test-Path $icow) {
        $icon = '-win32icon:' + $icow
        Write-Host "[build] icono del ejecutable: $icow"
    }
}

# Fuentes del ejecutable principal (con manifiesto requireAdministrator).
$exeOut = Join-Path $bin 'NetForge.exe'
$sourcesExe = @(
    'src\Program.cs',
    'src\SelfTestEntry.cs',
    'src\App\NetForgeContext.cs',
    'src\App\TrayIcon.cs',
    'src\App\FileShareService.cs',
    'src\App\SelfTest.cs',
    'src\Core\AppSettings.cs',
    'src\Core\Autostart.cs',
    'src\Core\CapabilityProbe.cs',
    'src\Core\DaemonGuard.cs',
    'src\Core\HotspotController.cs',
    'src\Core\IcsSharing.cs',
    'src\Core\InterfaceLocator.cs',
    'src\Core\MachineProfile.cs',
    'src\Core\ShellRunner.cs',
    'src\Core\Engines\WiFiDirectGoEngine.cs',
    'src\Core\Engines\MobileHotspotEngine.cs',
    'src\Core\Engines\LegacyHostedNetworkEngine.cs',
    'src\Firewall\FirewallRuleManager.cs',
    'src\Firewall\FirewallBanList.cs',
    'src\Ftp\FtpServer.cs',
    'src\Native\SystemApi.cs',
    'src\Native\WinRtAsync.cs',
    'src\Native\WlanApi.cs',
    'src\Net\LanClient.cs',
    'src\Net\OuiDatabase.cs',
    'src\Net\RadarService.cs',
    'src\Qr\QrEncoder.cs',
    'src\Qr\QrRenderer.cs',
    'src\Ui\Log.cs',
    'src\Ui\MainForm.cs',
    'src\Ui\Strings.cs',
    'src\Ui\Theme.cs',
    'src\Web\FilePortal.cs'
) | ForEach-Object { Join-Path $root $_ }Write-Host '[build] compilando NetForge.exe'
    $refs = @($framework + '\System.dll', $framework + '\System.Core.dll', $framework + '\System.Drawing.dll', $framework + '\System.Windows.Forms.dll', $framework + '\System.Net.dll')
    $cscArgs = @(-nologo, -target:exe, -langversion:latest, "-out=$exeOut", -platform:x64, "-win32manifest=src\App.manifest")
    if ($icon) { $cscArgs += $icon }
    $cscArgs += $embed + $sourcesExe
    foreach ($r in $refs) { $cscArgs += "-r:$r" }
    & $csc @cscArgs
    if ($LASTEXITCODE -ne 0) { throw '[build] NetForge.exe no se compiló' }

Write-Host "[build] NetForge.exe listo: $([math]::Round((Get-Item $exeOut).Length / 1KB)) KB"

# Fuentes del ejecutable de autocomprobacion (sin manifiesto de administrador).
if (-not $SkipSelfTest) {
    $autotestOut = Join-Path $bin 'NetForge.Autotest.exe'
    $sourcesTest = @(
        'src\SelfTestEntry.cs',
        'src\App\SelfTest.cs',
        'src\Core\AppSettings.cs',
        'src\Core\CapabilityProbe.cs',
        'src\Core\DaemonGuard.cs',
        'src\Core\HotspotController.cs',
        'src\Core\MachineProfile.cs',
        'src\Core\InterfaceLocator.cs',
        'src\Core\ShellRunner.cs',
        'src\Core\Engines\WiFiDirectGoEngine.cs',
        'src\Core\Engines\MobileHotspotEngine.cs',
        'src\Core\Engines\LegacyHostedNetworkEngine.cs',
        'src\Firewall\FirewallRuleManager.cs',
        'src\Firewall\FirewallBanList.cs',
        'src\Ftp\FtpServer.cs',
        'src\Native\SystemApi.cs',
        'src\Native\WinRtAsync.cs',
        'src\Native\WlanApi.cs',
        'src\Net\LanClient.cs',
        'src\Net\OuiDatabase.cs',
        'src\Net\RadarService.cs',
        'src\Qr\QrEncoder.cs',
        'src\Qr\QrRenderer.cs',
        'src\Ui\Log.cs',
        'src\Ui\MainForm.cs',
        'src\Ui\Strings.cs',
        'src\Ui\Theme.cs',
        'src\Web\FilePortal.cs'
    ) | ForEach-Object { Join-Path $root $_ }

    # El Autotest necesita System.Net.NetworkInformation (para comprobaciones de subred/ARP) y
    # System.Net.Sockets. Se linkean tambien System.Configuration para System.Net.HttpListener si
    # hiciera falta, pero netamente el portal usa HttpListener que entra por System.dll en Framework.
    Write-Host '[build] compilando NetForge.Autotest.exe'
    $refsTest = @($framework + '\System.dll', $framework + '\System.Core.dll', $framework + '\System.Drawing.dll', $framework + '\System.Windows.Forms.dll', $framework + '\System.Net.dll', $framework + '\System.Net.NetworkInformation.dll')
    $cscArgsTest = @(-nologo, -target:exe, -langversion:latest, "-out=$autotestOut", -platform:x64)
    $cscArgsTest += $sourcesTest
    foreach ($r in $refsTest) { $cscArgsTest += "-r:$r" }
    & $csc @cscArgsTest
    if ($LASTEXITCODE -ne 0) { throw '[build] NetForge.Autotest.exe no se compiló' }

    Write-Host "[build] NetForge.Autotest.exe listo: $([math]::Round((Get-Item $autotestOut).Length / 1KB)) KB"

    Write-Host '[build] ejecutando autocomprobacion'
    & $autotestOut
    if ($LASTEXITCODE -ne 0) { throw '[build] la autocomprobacion fallo' }
    Write-Host '[build] autocomprobacion OK'
} else {
    Write-Host '[build] autocomprobacion omitida (-SkipSelfTest)'
}

Write-Host '[build] terminado'
