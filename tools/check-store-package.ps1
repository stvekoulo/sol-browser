param([Parameter(Mandatory)][string]$Package)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.Drawing
$archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path $Package).Path)
try {
    $names = @($archive.Entries.FullName)
    if ('resources.pri' -notin $names) {
        throw 'MSIX WinUI invalide : resources.pri manque a la racine du paquet.'
    }
    if ('Sol.pri' -in $names) { throw 'Le PRI non empaquete Sol.pri ne doit pas remplacer resources.pri.' }
    foreach ($size in 16,20,24,30,32,36,40,48,60,64,72,80,96,256) {
        foreach ($form in '', '_altform-unplated', '_altform-lightunplated') {
            $asset = "StoreAssets/Square44x44Logo.targetsize-$size$form.png"
            if ($asset -notin $names) { throw "Variante d’icône Windows absente : $asset" }
        }
        $asset = "StoreAssets/FileLogo.targetsize-$size.png"
        if ($asset -notin $names) { throw "Variante d’icône de fichier absente : $asset" }
    }
    foreach ($required in 'Sol.exe', 'Microsoft.UI.Xaml.dll', 'AppxManifest.xml', 'Assets/Sol.ico', 'Assets/SolFile.ico') {
        if ($required -notin $names) { throw "Fichier requis absent : $required" }
    }
    $reader = [IO.StreamReader]::new($archive.GetEntry('AppxManifest.xml').Open())
    try { $manifest = [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
    $ns = [Xml.XmlNamespaceManager]::new($manifest.NameTable)
    foreach ($prefix in 'uap', 'uap3', 'desktop7') {
        $ns.AddNamespace($prefix, $manifest.DocumentElement.GetNamespaceOfPrefix($prefix))
    }
    $association = $manifest.SelectSingleNode("//uap3:FileTypeAssociation[@Name='solweb']", $ns)
    if ($association.SelectSingleNode('uap:Logo', $ns).InnerText -ne 'StoreAssets\FileLogo.png' -or
        $association.SelectSingleNode('desktop7:Logo', $ns).InnerText -ne 'Assets\SolFile.ico') {
        throw 'Icône multirésolution ou PNG de fichiers absent du manifeste.'
    }
    foreach ($asset in 'StoreLogo.png', 'Square44x44Logo.png', 'Square150x150Logo.png', 'Square44x44Logo.targetsize-32_altform-unplated.png', 'FileLogo.png', 'FileLogo.targetsize-256.png') {
        $resource = $archive.GetEntry("StoreAssets/$asset")
        if (-not $resource) { throw "Icône requise absente : $asset" }
        $stream = [IO.MemoryStream]::new()
        $entry = $resource.Open()
        try {
            $entry.CopyTo($stream)
            $stream.Position = 0
            $image = [Drawing.Bitmap]::new($stream)
            try {
                $top = $image.GetPixel([int]($image.Width / 2), [int]($image.Height / 8))
                if ($image.GetPixel(0, 0).A -ne 0 -or $top.A -ne 255 -or $top.R -lt 190) {
                    throw "Le fond argenté arrondi est incorrect pour $asset."
                }
                if ($asset -eq 'FileLogo.png') {
                    $left = $image.Width
                    $right = -1
                    for ($y = 0; $y -lt $image.Height; $y++) {
                        for ($x = 0; $x -lt $image.Width; $x++) {
                            $pixel = $image.GetPixel($x, $y)
                            if ($pixel.A -gt 200 -and $pixel.R -lt 120) {
                                $left = [Math]::Min($left, $x)
                                $right = [Math]::Max($right, $x)
                            }
                        }
                    }
                    if ($right - $left + 1 -lt 0.68 * $image.Width) { throw 'Le symbole de fichier est trop petit.' }
                }
            } finally { $image.Dispose() }
        } finally { $entry.Dispose(); $stream.Dispose() }
    }
    [pscustomobject]@{
        Package = (Resolve-Path $Package).Path
        Version = $manifest.Package.Identity.Version
        Architecture = $manifest.Package.Identity.ProcessorArchitecture
        Resources = 'resources.pri'
        SHA256 = (Get-FileHash $Package -Algorithm SHA256).Hash
    }
}
finally { $archive.Dispose() }
