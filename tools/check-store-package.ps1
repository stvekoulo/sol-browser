param([Parameter(Mandatory)][string]$Package)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
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
            if ($asset -notin $names) { throw "Icone Windows sans plaque absente : $asset" }
        }
    }
    foreach ($required in 'Sol.exe', 'Microsoft.UI.Xaml.dll', 'AppxManifest.xml') {
        if ($required -notin $names) { throw "Fichier requis absent : $required" }
    }
    $reader = [IO.StreamReader]::new($archive.GetEntry('AppxManifest.xml').Open())
    try { $manifest = [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
    [pscustomobject]@{
        Package = (Resolve-Path $Package).Path
        Version = $manifest.Package.Identity.Version
        Architecture = $manifest.Package.Identity.ProcessorArchitecture
        Resources = 'resources.pri'
        SHA256 = (Get-FileHash $Package -Algorithm SHA256).Hash
    }
}
finally { $archive.Dispose() }
