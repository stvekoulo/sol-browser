# Récupère uBlock Origin Lite (bloqueur de publicités, licence GPLv3) depuis son dépôt officiel et le dépose là où le
# navigateur l'embarque. À relancer pour changer de version : modifier $version.
# Le dossier obtenu n'est pas versionné (voir .gitignore) ; la compilation le copie à côté de l'exécutable.
$ErrorActionPreference = 'Stop'
$version = '2026.930.1227'
$url = "https://github.com/uBlockOrigin/uBOL-home/releases/download/$version/uBOLite_$version.edge.zip"
$target = Join-Path $PSScriptRoot '..\src\Browser.App\extensions\ubol'
$zip = Join-Path $env:TEMP "uBOLite_$version.zip"

Invoke-WebRequest $url -OutFile $zip
if (Test-Path $target) { [IO.Directory]::Delete($target, $true) }
Expand-Archive $zip -DestinationPath $target
[IO.File]::Delete($zip)
"uBlock Origin Lite $version -> $((Resolve-Path $target).Path)"
