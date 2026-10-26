param(
    [Parameter(Mandatory)][string]$AppDirectory,
    [Parameter(Mandatory)][string]$SdkDirectory
)
$ErrorActionPreference = 'Stop'
$app = (Resolve-Path $AppDirectory).Path
$root = Split-Path $PSScriptRoot
$work = Join-Path (Split-Path $app) ('resource-build-' + (Split-Path $app -Leaf))
New-Item -ItemType Directory -Path $work -Force | Out-Null
Add-Type -AssemblyName System.Drawing
$source = [Drawing.Bitmap]::new((Join-Path $root 'brand/logo-transparent.png'))
$files = [Collections.Generic.List[string]]::new()
try {
    foreach ($size in 16,20,24,30,32,36,40,48,60,64,72,80,96,256) {
        $image = [Drawing.Bitmap]::new($size, $size)
        $graphics = [Drawing.Graphics]::FromImage($image)
        try {
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.DrawImage($source, 0, 0, $size, $size)
            foreach ($form in '', '_altform-unplated', '_altform-lightunplated') {
                $relative = "StoreAssets\Square44x44Logo.targetsize-$size$form.png"
                $image.Save((Join-Path $app $relative), [Drawing.Imaging.ImageFormat]::Png)
                $files.Add($relative)
            }
        }
        finally { $graphics.Dispose(); $image.Dispose() }
    }
}
finally { $source.Dispose() }
foreach ($base in 'StoreLogo', 'Square44x44Logo', 'Square150x150Logo') {
    $files.Add("StoreAssets\$base.png")
}
$files | Set-Content (Join-Path $work 'icons.resfiles') -Encoding utf8
'resources.pri' | Set-Content (Join-Path $work 'winui.resfiles') -Encoding utf8
$icons = [Security.SecurityElement]::Escape((Join-Path $work 'icons.resfiles'))
$winui = [Security.SecurityElement]::Escape((Join-Path $work 'winui.resfiles'))
@"
<resources targetOsVersion="10.0.0" majorVersion="1">
  <index root="\" startIndexAt="$icons">
    <default><qualifier name="Language" value="en-US"/><qualifier name="Scale" value="100"/></default>
    <indexer-config type="RESFILES" qualifierDelimiter="."/>
  </index>
  <index root="\" startIndexAt="$winui">
    <indexer-config type="PRI"/>
    <indexer-config type="RESFILES" qualifierDelimiter="."/>
  </index>
</resources>
"@ | Set-Content (Join-Path $work 'priconfig.xml') -Encoding utf8
$output = Join-Path $work 'resources.pri'
& (Join-Path $SdkDirectory 'x64/makepri.exe') new /pr $app /cf (Join-Path $work 'priconfig.xml') /in Sol /of $output /o *> (Join-Path $work 'makepri.log')
if ($LASTEXITCODE) { throw "Indexation des icones Store en echec : $work/makepri.log" }
Copy-Item -LiteralPath $output -Destination (Join-Path $app 'resources.pri') -Force
