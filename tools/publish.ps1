# Produit la version publiable du navigateur : un dossier autonome (aucun .NET à installer chez l'utilisateur) et son
# archive zip. Tout est écrit sous D:\dev\navigateur-release.
#   pwsh tools\publish.ps1            version lue dans le projet
# Seul le moteur web de Windows (WebView2 Runtime, présent d'origine sur Windows 11) reste requis sur la machine.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'env.ps1')
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$project = Join-Path $root 'src\Browser.App\Browser.App.csproj'
$version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "Version absente de $project" }
$out = 'D:\dev\navigateur-release'
$app = Join-Path $out 'app'

# Le bloqueur de publicités livré avec le navigateur (non versionné) doit être là.
if (-not (Test-Path (Join-Path $root 'src\Browser.App\extensions\ubol\manifest.json'))) { & (Join-Path $PSScriptRoot 'get-ublock.ps1') }

Push-Location (Join-Path $root 'ui')
try { npm run build; if ($LASTEXITCODE -ne 0) { throw 'Interface : compilation en échec' } } finally { Pop-Location }

dotnet test (Join-Path $root 'tests\Browser.Core.Tests') -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Tests en échec : rien n''est publié' }

if (Test-Path $app) { [IO.Directory]::Delete($app, $true) }
dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o $app --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Publication en échec' }
Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.md') $app

$zip = Join-Path $out "Navigateur-$version-win-x64.zip"
if (Test-Path $zip) { [IO.File]::Delete($zip) }
Compress-Archive -Path (Join-Path $app '*') -DestinationPath $zip
$size = [math]::Round(((Get-ChildItem $app -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB)
"Navigateur $version : $app ($size Mo), archive $([math]::Round((Get-Item $zip).Length / 1MB)) Mo -> $zip"
