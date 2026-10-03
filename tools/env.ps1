# À charger dans chaque shell de dev : . D:\projet-navigateur\tools\env.ps1
# Garde tous les outils, caches et données de dev hors de C: (disque plein).
$dev = 'D:\dev'
foreach ($d in 'dotnet','dotnet-home','nuget-packages','npm-cache','tmp','webview2-data','navigateur-data') {
    New-Item -ItemType Directory -Force "$dev\$d" | Out-Null
}
$env:DOTNET_ROOT = "$dev\dotnet"
$env:DOTNET_CLI_HOME = "$dev\dotnet-home"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:NUGET_PACKAGES = "$dev\nuget-packages"
$env:NUGET_HTTP_CACHE_PATH = "$dev\nuget-http-cache"
$env:NUGET_PLUGINS_CACHE_PATH = "$dev\nuget-plugins-cache"
$env:npm_config_cache = "$dev\npm-cache"
$env:TEMP = "$dev\tmp"
$env:TMP = "$dev\tmp"
$env:WEBVIEW2_USER_DATA_FOLDER = "$dev\webview2-data"
$env:NAVIGATEUR_DATA_DIR = "$dev\navigateur-data"
# Comparaison exacte : le PATH peut déjà contenir D:\dev\dotnet-home\.dotnet\tools, qui n'est pas le SDK.
if (($env:PATH -split ';') -notcontains "$dev\dotnet") { $env:PATH = "$dev\dotnet;$env:PATH" }
