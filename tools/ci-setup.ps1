$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$localDotnet = 'D:\dev\dotnet\dotnet.exe'

# The local runner already has the SDK; downloading it again makes releases depend on Microsoft's download server.
if ((Test-Path -LiteralPath $localDotnet) -and (& $localDotnet --list-sdks | Where-Object { $_ -match '^10\.' })) {
    . (Join-Path $PSScriptRoot 'env.ps1')
    Write-Host 'Using the installed local .NET SDK.'
} else {
    Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile "$env:TEMP\dotnet-install.ps1" -TimeoutSec 120
    & "$env:TEMP\dotnet-install.ps1" -Channel 10.0 -InstallDir "$projectRoot\.dotnet"
    if ($LASTEXITCODE -ne 0) { throw '.NET SDK installation failed' }
    $env:DOTNET_ROOT = "$projectRoot\.dotnet"
}
$toolHome = if ($env:DOTNET_CLI_HOME) { $env:DOTNET_CLI_HOME } else { $env:USERPROFILE }
$env:PATH = "$env:DOTNET_ROOT;$toolHome\.dotnet\tools;$env:USERPROFILE\.dotnet\tools;$env:PATH"
$sdk = & dotnet --version
if ($LASTEXITCODE -ne 0 -or $sdk -notmatch '^10\.') { throw "Expected a .NET 10 SDK, got '$sdk'" }
Write-Host ".NET SDK: $sdk"

$nodeCommand = Get-Command node -ErrorAction SilentlyContinue
$nodeVersion = if ($nodeCommand) { & node --version } else { '' }
if ($nodeVersion -notmatch '^v\d+\.\d+\.\d+$' -or [version]$nodeVersion.TrimStart('v') -lt [version]'22.12.0') {
    $node = ((Invoke-RestMethod https://nodejs.org/dist/index.json -TimeoutSec 120) | Where-Object { $_.lts } | Select-Object -First 1).version
    if ($node -notmatch '^v\d+\.\d+\.\d+$') { throw "Unexpected Node.js version '$node'" }
    Invoke-WebRequest "https://nodejs.org/dist/$node/node-$node-win-x64.zip" -OutFile "$env:TEMP\node.zip" -TimeoutSec 300
    Expand-Archive "$env:TEMP\node.zip" "$env:TEMP\node" -Force
    $env:PATH = "$env:TEMP\node\node-$node-win-x64;$env:PATH"
}
node --version
if ($LASTEXITCODE -ne 0) { throw 'Node.js setup failed' }
if (-not (Get-Command pwsh -ErrorAction SilentlyContinue)) {
    dotnet tool install -g PowerShell
    if ($LASTEXITCODE -ne 0) { throw 'PowerShell setup failed' }
}
