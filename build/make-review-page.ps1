param([string[]]$Names = @('es-ES-Clientes','es-ES-Indultos','es-ES-Archivos','es-ES-Ajustes','es-ES-Diagnostico','es-ES-Registro','en-US-Settings','en-US-Clients'), [string]$Out = 'site/_rev4.html')
$root = Split-Path -Parent $PSCommandPath
Set-Location (Join-Path $root '..')
$sb = New-Object System.Text.StringBuilder
[void]$sb.Append('<!DOCTYPE html><html lang="es"><head><meta charset="utf-8"><title>Revision</title><style>html,body{margin:0;background:#0d1117;color:#e6edf3;font:12px "Segoe UI",sans-serif}section{margin:0}header{padding:3px 8px;background:#161b22;color:#58a6ff;font-weight:600}img{display:block}</style></head><body>')
[void]$sb.Append('<script>function go(id,x,y){var e=document.getElementById(id);if(!e){return "sin imagen";}window.scrollTo(e.offsetLeft+(x||0),e.offsetTop+(y||0));return id+" @"+(x||0)+","+(y||0);}</script>')
foreach ($n in $Names) {
    $p = Join-Path 'site/_rev' ($n + '.png')
    if (-not (Test-Path $p)) { continue }
    $b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($p))
    [void]$sb.Append('<section><header>' + $n + '</header><img id="' + $n + '" alt="' + $n + '" src="data:image/png;base64,' + $b64 + '"></section>')
}
[void]$sb.Append('</body></html>')
[IO.File]::WriteAllText($Out, $sb.ToString())
Write-Host ('generado ' + $Out + ': ' + (Get-Item $Out).Length + ' bytes')
