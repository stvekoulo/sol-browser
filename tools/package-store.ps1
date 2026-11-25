# Construit des MSIX non signés pour Partner Center ; aucune installation ni modification des certificats.
param(
    [ValidateSet('x64', 'arm64', 'x86')][string[]]$Arch = @('x64', 'arm64', 'x86'),
    [string]$Out,
    [ValidatePattern('^[1-9][0-9]*\.[0-9]+\.[0-9]+\.0$')][string]$StoreVersion,
    [switch]$SkipUiBuild,
    [string]$SdkDirectory
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$devTools = Join-Path (Split-Path $root -Qualifier) 'dev\dotnet'
if (Test-Path $devTools) { . (Join-Path $PSScriptRoot 'env.ps1') }
if (-not $Out) { $Out = Join-Path $root ('bin\store-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
New-Item -ItemType Directory -Path $Out -ErrorAction Stop | Out-Null
$Out = (Resolve-Path $Out).Path
$sdkRoots = @('C:\Program Files (x86)\Windows Kits\10\bin')
if ($env:NUGET_PACKAGES) {
    $sdkRoots += @(Get-ChildItem (Join-Path $env:NUGET_PACKAGES 'microsoft.windows.sdk.buildtools') -Directory -ErrorAction SilentlyContinue |
        ForEach-Object { Join-Path $_.FullName 'bin' })
}
$sdk = if ($SdkDirectory) { Get-Item -LiteralPath $SdkDirectory } else {
    $sdkRoots | Where-Object { Test-Path $_ } | ForEach-Object { Get-ChildItem -LiteralPath $_ -Directory } |
    Where-Object { $_.Name -match '^10\.' -and (Test-Path (Join-Path $_.FullName 'x64\makeappx.exe')) } |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
}
if (-not $sdk) { throw 'MakeAppx du SDK Windows est requis.' }
$makeappx = Join-Path $sdk.FullName 'x64\makeappx.exe'
if (-not $SkipUiBuild) {
    Push-Location (Join-Path $root 'ui')
    try { npm.cmd run build; if ($LASTEXITCODE) { throw 'Compilation UI en échec.' } }
    finally { Pop-Location }
}
Add-Type -AssemblyName System.Drawing
$bitmap = [Drawing.Bitmap]::new((Join-Path $root 'brand\logo-windows.png'))
try {
    foreach ($a in $Arch) {
        $app = Join-Path $Out "app-$a"
        $platform = @{ x64 = 'x64'; arm64 = 'ARM64'; x86 = 'x86' }[$a]
        dotnet publish (Join-Path $root 'src\Browser.App\Browser.App.csproj') -c Release -r "win-$a" `
            "-p:Platform=$platform" -p:SolStoreBuild=true --self-contained true -p:PublishReadyToRun=true `
            -o $app --nologo -v q
        if ($LASTEXITCODE) { throw "Compilation Store $a en échec." }
        # Sous identite MSIX, WinUI cherche resources.pri (Sol.pri est le nom du build non empaquete).
        $pri = Join-Path $app 'Sol.pri'
        if (-not (Test-Path $pri)) { throw "Index WinUI absent : $pri" }
        Move-Item -LiteralPath $pri -Destination (Join-Path $app 'resources.pri') -Force
        if (Get-ChildItem (Join-Path $app 'ui\assets') -Filter '*SFPro*' -ErrorAction SilentlyContinue) {
            throw "Une police Apple reste embarquée : reconstruire l’interface avant la distribution."
        }
        foreach ($notice in 'THIRD-PARTY-NOTICES.md', 'LICENSE') {
            Copy-Item (Join-Path $root $notice) $app
        }
        # Allège les MSIX sans toucher aux DLL ONNX Runtime requises par la voix locale sur x64.
        $unused = if ($a -ne 'x64') { @('onnxruntime.dll', 'onnxruntime_providers_shared.dll', 'DirectML.dll') } else { @('DirectML.dll') }
        foreach ($name in $unused) {
            $file = Join-Path $app $name
            if (Test-Path -LiteralPath $file) { [IO.File]::Delete($file) }
        }
        foreach ($pattern in 'onnxruntime*.lib', 'onnxruntime*.pdb', 'Microsoft.ML.OnnxRuntime*.pdb', 'DirectML.pdb') {
            Get-ChildItem -LiteralPath $app -Filter $pattern -File -ErrorAction SilentlyContinue | Remove-Item -Force
        }
        if ($a -ne 'x64') {
            Get-ChildItem -LiteralPath $app -Filter '*.lib' -File -ErrorAction SilentlyContinue | Remove-Item -Force
        }
        $assets = Join-Path $app 'StoreAssets'
        New-Item -ItemType Directory $assets | Out-Null
        foreach ($asset in @{ StoreLogo = 50; Square44x44Logo = 44; Square150x150Logo = 150; FileLogo = 44 }.GetEnumerator()) {
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
        $packageVersion = if ($StoreVersion) { $StoreVersion } else { "$version.0" }
        $manifest.Package.Identity.Version = $packageVersion
        $manifest.Save((Join-Path $app 'AppxManifest.xml'))
        & (Join-Path $PSScriptRoot 'prepare-store-icons.ps1') -AppDirectory $app -SdkDirectory $sdk.FullName
        $package = Join-Path $Out "SolNavigateur-$packageVersion-$a.msix"
        & $makeappx pack /d $app /p $package /o *> (Join-Path $Out "makeappx-$a.log")
        if ($LASTEXITCODE) { throw "Validation/paquet MSIX $a en échec : consulter makeappx-$a.log." }
        & (Join-Path $PSScriptRoot 'check-store-package.ps1') -Package $package | Out-Null
        [pscustomobject]@{ File = $package; SHA256 = (Get-FileHash $package).Hash; Signed = $false } |
            ConvertTo-Json | Set-Content (Join-Path $Out "verification-$a.json")
    }
}
finally { $bitmap.Dispose() }
Write-Host "Paquets validés par MakeAppx : $Out"
