param([switch]$BrowserOnly, [string]$LandingDirectory)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$logo = Join-Path $root 'brand\logo-transparent.png'
if (-not (Test-Path -LiteralPath $logo)) { throw 'Le logo transparent est requis avant de synchroniser les icônes.' }
Add-Type -AssemblyName System.Drawing

function Save-ResizedImage($Source, $Path, $Width, $Height, $Codec, $Parameters) {
    $bitmap = [Drawing.Bitmap]::new($Width, $Height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.DrawImage($Source, 0, 0, $Width, $Height)
        $bitmap.Save($Path, $Codec, $Parameters)
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
}

$uiAssets = Join-Path $root 'ui\src\assets'
$jpeg = [Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object MimeType -eq 'image/jpeg'
$quality = [Drawing.Imaging.EncoderParameters]::new(1)
$quality.Param[0] = [Drawing.Imaging.EncoderParameter]::new([Drawing.Imaging.Encoder]::Quality, [long]90)
try {
    foreach ($suffix in '', '-dark') {
        $image = [Drawing.Image]::FromFile((Join-Path $root "brand\wallpaper${suffix}-original.png"))
        try {
            $image.Save((Join-Path $uiAssets "start-background${suffix}.jpg"), $jpeg, $quality)
            Save-ResizedImage $image (Join-Path $uiAssets "start-background${suffix}-thumb.jpg") 256 144 $jpeg $quality
        } finally { $image.Dispose() }
    }
    $cover = [Drawing.Image]::FromFile((Join-Path $root 'brand\logo-original.png'))
    try {
        Save-ResizedImage $cover (Join-Path $uiAssets 'logo-welcome.jpg') 256 256 $jpeg $quality
    } finally { $cover.Dispose() }
} finally { $quality.Dispose() }
function Save-Icon($Source, $Path, [switch]$ExportBadge) {
    $entries = @()
    foreach ($size in 16, 24, 32, 48, 64, 72, 96, 128, 256) {
        $bitmap = [Drawing.Bitmap]::new($size, $size)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $stream = [IO.MemoryStream]::new()
        $badge = [Drawing.Drawing2D.GraphicsPath]::new()
        $fill = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(232, 232, 235))
        try {
            $diameter = [int]($size * 0.46)
            $edge = $size - $diameter - 1
            $badge.AddArc(1, 1, $diameter, $diameter, 180, 90)
            $badge.AddArc($edge, 1, $diameter, $diameter, 270, 90)
            $badge.AddArc($edge, $edge, $diameter, $diameter, 0, 90)
            $badge.AddArc(1, $edge, $diameter, $diameter, 90, 90)
            $badge.CloseFigure()
            $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $graphics.FillPath($fill, $badge)
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.DrawImage($source, 0, 0, $size, $size)
            $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
            $entries += @{ Size = $size; Bytes = $stream.ToArray() }
            if ($ExportBadge -and $size -eq 256) {
                [IO.File]::WriteAllBytes((Join-Path $root 'brand\logo-windows.png'), $entries[-1].Bytes)
            }
        } finally { $fill.Dispose(); $badge.Dispose(); $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
    }
    $output = [IO.MemoryStream]::new()
    $writer = [IO.BinaryWriter]::new($output)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$entries.Count)
        $offset = 6 + 16 * $entries.Count
        foreach ($entry in $entries) {
            $dimension = if ($entry.Size -eq 256) { 0 } else { $entry.Size }
            $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
            $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$entry.Bytes.Length); $writer.Write([uint32]$offset)
            $offset += $entry.Bytes.Length
        }
        foreach ($entry in $entries) { $writer.Write([byte[]]$entry.Bytes) }
        [IO.File]::WriteAllBytes($Path, $output.ToArray())
    } finally { $writer.Dispose(); $output.Dispose() }
}

$appAssets = Join-Path $root 'src\Browser.App\Assets'
$appIcon = Join-Path $appAssets 'Sol.ico'
$source = [Drawing.Image]::FromFile($logo)
try {
    Save-Icon $source $appIcon -ExportBadge
    Copy-Item -LiteralPath $appIcon -Destination (Join-Path $appAssets 'SolFile.ico') -Force
    if ($LandingDirectory) {
        $landing = (Resolve-Path -LiteralPath $LandingDirectory).Path
        Save-Icon $source (Join-Path $landing 'public\favicon.ico')
    }
    $splash = [Drawing.Bitmap]::new(256, 256)
    $g = [Drawing.Graphics]::FromImage($splash)
    try {
        $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.DrawImage($source, 0, 0, 256, 256)
        $splash.Save((Join-Path $root 'src\Browser.App\Assets\setup-splash.png'), [Drawing.Imaging.ImageFormat]::Png)
        # Logo de l'interface : affiché en 104 px au plus ; l'original de 1254 px coûtait 6 Mo décodés par vue.
        $splash.Save((Join-Path $root 'ui\src\assets\logo.png'), [Drawing.Imaging.ImageFormat]::Png)
    } finally { $g.Dispose(); $splash.Dispose() }
    # Logo de la page d'accueil : affiché en 44 px, net jusqu'à 300 %, sans décoder l'original à chaque nouvel onglet.
    $small = [Drawing.Bitmap]::new(132, 132)
    $g = [Drawing.Graphics]::FromImage($small)
    try {
        $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.DrawImage($source, 0, 0, 132, 132)
        $small.Save((Join-Path $root 'ui\src\assets\logo-start.png'), [Drawing.Imaging.ImageFormat]::Png)
    } finally { $g.Dispose(); $small.Dispose() }
} finally { $source.Dispose() }

$badge = Join-Path $root 'brand\logo-windows.png'
if (-not $BrowserOnly) {
    foreach ($target in 'docs\images\logo.png', 'docs\launch-film\assets\logo.png') {
        Copy-Item -LiteralPath $badge -Destination (Join-Path $root $target) -Force
    }
    $filmAssets = Join-Path $root 'docs\launch-film\src\assets.js'
    $bundle = [IO.File]::ReadAllText($filmAssets)
    $logoPattern = '"logo":"data:image/png;base64,[A-Za-z0-9+/=]+"'
    if ([regex]::Matches($bundle, $logoPattern).Count -ne 1) { throw 'Logo introuvable dans les ressources du film.' }
    $encoded = [Convert]::ToBase64String([IO.File]::ReadAllBytes($badge))
    [IO.File]::WriteAllText($filmAssets, ($bundle -creplace $logoPattern, ('"logo":"data:image/png;base64,' + $encoded + '"')))
}
if ($LandingDirectory) {
    foreach ($target in 'public\logo.png', 'src\app\icon.png', 'src\app\apple-icon.png') {
        Copy-Item -LiteralPath $badge -Destination (Join-Path $landing $target) -Force
    }
    Copy-Item -LiteralPath (Join-Path $uiAssets 'start-background-dark.jpg') -Destination (Join-Path $landing 'public\wallpaper-dark.jpg') -Force
}
Write-Host 'Identité Sol synchronisée ; originaux conservés.'
