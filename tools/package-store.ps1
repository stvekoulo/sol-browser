# Construit des MSIX non signés pour Partner Center ; aucune installation ni modification des certificats.
param(
    [ValidateSet('x64', 'arm64', 'x86')][string[]]$Arch = @('x64', 'arm64', 'x86'),
    [string]$Out,
    [switch]$SkipUiBuild
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (Test-Path 'D:\dev\dotnet') { . (Join-Path $PSScriptRoot 'env.ps1') }
if (-not $Out) { $Out = Join-Path $root ('bin\store-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
New-Item -ItemType Directory -Path $Out -ErrorAction Stop | Out-Null
$Out = (Resolve-Path $Out).Path
$sdk = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Directory |
    Where-Object { $_.Name -match '^10\.' -and (Test-Path (Join-Path $_.FullName 'x64\makeappx.exe')) } |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (-not $sdk) { throw 'MakeAppx du SDK Windows est requis.' }
$makeappx = Join-Path $sdk.FullName 'x64\makeappx.exe'
if (-not $SkipUiBuild) {
    Push-Location (Join-Path $root 'ui')
    try { npm.cmd run build; if ($LASTEXITCODE) { throw 'Compilation UI en échec.' } }
    finally { Pop-Location }
}
Add-Type -AssemblyName System.Drawing
$icon = [Drawing.Icon]::new((Join-Path $root 'src\Browser.App\Assets\Sol.ico'), 256, 256)
$bitmap = $icon.ToBitmap()
if ($bitmap.GetPixel(0, 0).A -ne 0) {
    $bitmap.Dispose(); $icon.Dispose()
    throw 'Le logo Windows doit conserver son fond transparent avant la distribution Store.'
}
try {
    foreach ($a in $Arch) {
        $app = Join-Path $Out "app-$a"
        $platform = @{ x64 = 'x64'; arm64 = 'ARM64'; x86 = 'x86' }[$a]
        dotnet publish (Join-Path $root 'src\Browser.App\Browser.App.csproj') -c Release -r "win-$a" `
            "-p:Platform=$platform" -p:SolStoreBuild=true --self-contained true -p:PublishReadyToRun=true `
            -o $app --nologo -v q
        if ($LASTEXITCODE) { throw "Compilation Store $a en échec." }
        if (Get-ChildItem (Join-Path $app 'ui\assets') -Filter '*SFPro*' -ErrorAction SilentlyContinue) {
            throw "Une police Apple reste embarquée : reconstruire l’interface avant la distribution."
        }
        foreach ($notice in 'THIRD-PARTY-NOTICES.md', 'LICENSE') {
            Copy-Item (Join-Path $root $notice) $app
        }
        $assets = Join-Path $app 'StoreAssets'
        New-Item -ItemType Directory $assets | Out-Null
        foreach ($asset in @{ StoreLogo = 50; Square44x44Logo = 44; Square150x150Logo = 150 }.GetEnumerator()) {
            $image = [Drawing.Bitmap]::new($asset.Value, $asset.Value)
            $graphics = [Drawing.Graphics]::FromImage($image)
            try {
                $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.DrawImage($bitmap, 0, 0, $asset.Value, $asset.Value)
                $image.Save((Join-Path $assets "$($asset.Key).png"), [Drawing.Imaging.ImageFormat]::Png)
            }
            finally { $graphics.Dispose(); $image.Dispose() }
        }
        $manifest = [xml](Get-Content (Join-Path $root 'store\AppxManifest.xml') -Raw)
        $manifest.Package.Identity.ProcessorArchitecture = $a
        $version = ([xml](Get-Content (Join-Path $root 'src\Browser.App\Browser.App.csproj') -Raw)).Project.PropertyGroup.Version |
            Where-Object { $_ } | Select-Object -First 1
        $manifest.Package.Identity.Version = "$version.0"
        $manifest.Save((Join-Path $app 'AppxManifest.xml'))
        $package = Join-Path $Out "SolNavigateur-$version-$a.msix"
        & $makeappx pack /d $app /p $package /o *> (Join-Path $Out "makeappx-$a.log")
        if ($LASTEXITCODE) { throw "Validation/paquet MSIX $a en échec : consulter makeappx-$a.log." }
        [pscustomobject]@{ File = $package; SHA256 = (Get-FileHash $package).Hash; Signed = $false } |
            ConvertTo-Json | Set-Content (Join-Path $Out "verification-$a.json")
    }
}
finally { $bitmap.Dispose(); $icon.Dispose() }
Write-Host "Paquets validés par MakeAppx : $Out"
