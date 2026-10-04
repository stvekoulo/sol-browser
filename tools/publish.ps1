# Produit les versions publiables du navigateur : pour chaque architecture, un dossier autonome (aucun .NET à installer
# chez l'utilisateur), son archive zip, et le fichier des empreintes SHA-256 de toutes les archives.
#   pwsh tools\publish.ps1                         x64 seulement
#   pwsh tools\publish.ps1 -Arch x64,arm64,x86     les trois architectures de Windows
#   pwsh tools\publish.ps1 -Out release            dossier de sortie (sinon D:\dev\sol-release sur le poste de dev)
# Seul le moteur web de Windows (WebView2 Runtime, présent d'origine sur Windows 11) reste requis sur la machine.
param(
    [string[]]$Arch = @('x64'),
    [string]$Out
)
$ErrorActionPreference = 'Stop'
# « x64,arm64 » arrive en un seul mot quand le script est lancé par « pwsh -File » : on le découpe ici.
$Arch = $Arch | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim().ToLower() } | Where-Object { $_ }
$unknown = $Arch | Where-Object { $_ -notin 'x64', 'arm64', 'x86' }
if ($unknown) { throw "Architecture inconnue : $unknown (x64, arm64 ou x86)" }
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
# Poste de développement : outils et caches sont sur D: (voir env.ps1). Ailleurs (intégration continue), ceux du système.
$dev = Test-Path 'D:\dev\dotnet'
if ($dev) { . (Join-Path $PSScriptRoot 'env.ps1') }
if (-not $Out) { $Out = if ($dev) { 'D:\dev\sol-release' } else { Join-Path $root 'release' } }
New-Item -ItemType Directory -Force $Out | Out-Null
$Out = (Resolve-Path $Out).Path

$project = Join-Path $root 'src\Browser.App\Browser.App.csproj'
$version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "Version absente de $project" }

# Le bloqueur de publicités livré avec le navigateur (non versionné) doit être là.
if (-not (Test-Path (Join-Path $root 'src\Browser.App\extensions\ubol\manifest.json'))) { & (Join-Path $PSScriptRoot 'get-ublock.ps1') }

Push-Location (Join-Path $root 'ui')
try { npm run build; if ($LASTEXITCODE -ne 0) { throw 'Interface : compilation en échec' } } finally { Pop-Location }

dotnet test (Join-Path $root 'tests\Browser.Core.Tests') -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Tests en échec : rien n''est publié' }

$platforms = @{ x64 = 'x64'; arm64 = 'ARM64'; x86 = 'x86' }
foreach ($a in $Arch) {
    $app = Join-Path $Out "app-$a"
    if (Test-Path $app) { [IO.Directory]::Delete($app, $true) }
    dotnet publish $project -c Release -r "win-$a" "-p:Platform=$($platforms[$a])" --self-contained true -p:PublishReadyToRun=true -o $app --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "Publication $a en échec" }
    foreach ($notice in 'THIRD-PARTY-NOTICES.md', 'LICENSE') {
        $file = Join-Path $root $notice
        if (Test-Path $file) { Copy-Item $file $app }
    }

    # Bibliothèques d'intelligence artificielle de Windows, livrées d'office avec le kit d'interface et dont le
    # navigateur ne se sert pas : une quarantaine de Mo de moins. (Vérifié : il démarre et fonctionne sans elles.)
    foreach ($unused in 'onnxruntime.dll', 'DirectML.dll') {
        $file = Join-Path $app $unused
        if (Test-Path $file) { [IO.File]::Delete($file) }
    }

    $zip = Join-Path $Out "Sol-$version-win-$a.zip"
    if (Test-Path $zip) { [IO.File]::Delete($zip) }
    Compress-Archive -Path (Join-Path $app '*') -DestinationPath $zip
    $size = [math]::Round(((Get-ChildItem $app -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB)
    Write-Host "Sol $version $a : $size Mo, archive $([math]::Round((Get-Item $zip).Length / 1MB)) Mo -> $zip"
}

# Empreintes : chacun peut vérifier que l'archive téléchargée est bien celle qui a été publiée.
$sums = Join-Path $Out 'SHA256SUMS.txt'
Get-ChildItem $Out -Filter "Sol-$version-win-*.zip" | Sort-Object Name |
    ForEach-Object { "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower())  $($_.Name)" } | Set-Content $sums -Encoding utf8
Write-Host "Empreintes -> $sums"
