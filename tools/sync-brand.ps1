# Conserve les originaux ; distribue uniquement le logo détouré et le fond fourni.
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$logo = Join-Path $root 'brand\logo-transparent.png'
if (-not (Test-Path -LiteralPath $logo)) { throw 'Le logo transparent est requis avant de synchroniser les icônes.' }
$wallpaper = Join-Path $root 'brand\wallpaper-original.png'
foreach ($target in 'ui\src\assets\logo.png', 'docs\images\logo.png', 'docs\launch-film\assets\logo.png', 'landing\public\logo.png', 'landing\src\app\icon.png', 'landing\src\app\apple-icon.png') {
    Copy-Item -LiteralPath $logo -Destination (Join-Path $root $target) -Force
}
foreach ($target in 'ui\src\assets\start-background.png', 'landing\public\wallpaper.png') {
    Copy-Item -LiteralPath $wallpaper -Destination (Join-Path $root $target) -Force
}
Add-Type -AssemblyName System.Drawing
$source = [Drawing.Image]::FromFile($logo)
$entries = @()
try {
    foreach ($size in 16, 24, 32, 48, 64, 128, 256) {
        $bitmap = [Drawing.Bitmap]::new($size, $size)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $stream = [IO.MemoryStream]::new()
        try {
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.DrawImage($source, 0, 0, $size, $size)
            $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
            $entries += @{ Size = $size; Bytes = $stream.ToArray() }
        } finally { $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
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
        foreach ($target in 'src\Browser.App\Assets\Sol.ico', 'landing\public\favicon.ico') {
            [IO.File]::WriteAllBytes((Join-Path $root $target), $output.ToArray())
        }
    } finally { $writer.Dispose(); $output.Dispose() }
    $splash = [Drawing.Bitmap]::new(256, 256)
    $g = [Drawing.Graphics]::FromImage($splash)
    try {
        $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.DrawImage($source, 0, 0, 256, 256)
        $splash.Save((Join-Path $root 'src\Browser.App\Assets\setup-splash.png'), [Drawing.Imaging.ImageFormat]::Png)
    } finally { $g.Dispose(); $splash.Dispose() }
} finally { $source.Dispose() }
Write-Host 'Identité Sol synchronisée ; originaux conservés.'
