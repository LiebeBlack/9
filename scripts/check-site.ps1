# SPDX-License-Identifier: GPL-3.0-or-later
# NetForge Studio - comprobacion del sitio web.
#
# El sitio es una sola pagina autocontenida con 219 pares de textos en espanol e ingles. Un
# par desparejado no da error en el navegador: simplemente deja un texto en el idioma
# equivocado y nadie se entera hasta que lo ve un visitante. Esta comprobacion lo detecta
# antes, y ademas vigila las dos promesas del proyecto que el sitio tambien hace: nada de
# dependencias externas y nada de recursos que se carguen desde internet.
[CmdletBinding()]
param(
    [string]$Path = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
if (-not $Path) {
    $Path = Join-Path $root 'site\index.html'
}

if (-not (Test-Path $Path)) {
    throw "[sitio] no existe $Path"
}

$html = Get-Content -Raw -Encoding UTF8 $Path
$problems = New-Object System.Collections.Generic.List[string]

# 1) Cada etiqueta traducida tiene que llevar los dos idiomas, y del mismo tipo.
#
# Las etiquetas se recortan con un escaner propio y no con una expresion regular: los textos
# con formato llevan dentro codigo HTML ("<strong>..."), y buscar el primer ">" cortaria la
# etiqueta a la mitad y daria fallos que no existen.
function Get-Tags {
    param([string]$Text)

    $list = New-Object System.Collections.Generic.List[string]
    $start = -1
    $quote = [char]0

    for ($i = 0; $i -lt $Text.Length; $i++) {
        $c = $Text[$i]

        if ($quote -ne [char]0) {
            if ($c -eq $quote) { $quote = [char]0 }
            continue
        }

        if ($c -eq '"' -or $c -eq "'") {
            if ($start -ge 0) { $quote = $c }
            continue
        }

        if ($c -eq '<') {
            $start = $i
            continue
        }

        if ($c -eq '>' -and $start -ge 0) {
            $list.Add($Text.Substring($start, $i - $start + 1))
            $start = -1
        }
    }

    return $list
}

$tags = Get-Tags -Text $html
$plainEs = 0
$plainEn = 0
$htmlEs = 0
$htmlEn = 0

foreach ($text in $tags) {
    if ($text -notmatch 'data-(es|en)\b') {
        continue
    }

    $hasPlainEs = $text -match 'data-es="'
    $hasPlainEn = $text -match 'data-en="'
    $hasHtmlEs = $text -match 'data-es-html="'
    $hasHtmlEn = $text -match 'data-en-html="'

    if ($hasPlainEs) { $plainEs++ }
    if ($hasPlainEn) { $plainEn++ }
    if ($hasHtmlEs) { $htmlEs++ }
    if ($hasHtmlEn) { $htmlEn++ }

    if ($hasPlainEs -ne $hasPlainEn) {
        $problems.Add("etiqueta <$($text.Split(' ')[0].TrimStart('<'))> con data-es pero sin data-en: $text")
    }

    if ($hasHtmlEs -ne $hasHtmlEn) {
        $problems.Add("etiqueta con data-es-html pero sin data-en-html: $text")
    }

    if (($hasPlainEs -or $hasPlainEn) -and ($hasHtmlEs -or $hasHtmlEn)) {
        $problems.Add("etiqueta con los dos formatos de traduccion a la vez: $text")
    }
}

if ($plainEs -ne $plainEn) {
    $problems.Add("textos simples desparejados: $plainEs en espanol frente a $plainEn en ingles")
}

if ($htmlEs -ne $htmlEn) {
    $problems.Add("textos con formato desparejados: $htmlEs en espanol frente a $htmlEn en ingles")
}

# 2) Sin dependencias externas: ni scripts, ni estilos, ni tipografias, ni imagenes remotas.
$external = [regex]::Matches($html, '(?:src|href)="(https?:)?//[^"]+"')
foreach ($match in $external) {
    if ($match.Value -match 'href="#') {
        continue
    }

    # Los enlaces al repositorio y a la descarga son navegacion, no dependencias: el unico
    # caso permitido es un <a href> con la constante del proyecto, que ademas el script
    # sobrescribe en tiempo de ejecucion.
    if ($match.Value -match 'href="https://github.com/TU-USUARIO') {
        continue
    }

    $problems.Add("recurso externo prohibido: $($match.Value)")
}

if ($html -match '<link[^>]+rel="stylesheet"') {
    $problems.Add('hoja de estilos externa: el sitio debe llevarlo todo dentro')
}

if ($html -match '<script[^>]+src=') {
    $problems.Add('script externo: el sitio debe llevarlo todo dentro')
}

# 3) Lo minimo que debe contar: si alguien borra una seccion sin querer, esto lo dice.
$requiredSections = @('problema', 'funciones', 'como', 'uso', 'compatibilidad', 'limites', 'verificacion', 'compilar', 'faq', 'descarga')
foreach ($section in $requiredSections) {
    if ($html -notmatch ('id="' + $section + '"')) {
        $problems.Add("falta la seccion #$section")
    }
}

if ($problems.Count -gt 0) {
    foreach ($problem in $problems) {
        Write-Host "[sitio] FALLO: $problem"
    }

    throw "[sitio] $($problems.Count) problema(s) en $Path"
}

$size = [math]::Round((Get-Item $Path).Length / 1KB)
Write-Host "[sitio] OK: $plainEs pares de texto, $htmlEs con formato, 0 dependencias externas ($size KB)"
