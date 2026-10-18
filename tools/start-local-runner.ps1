# Register once with a project runner token saved locally, then run without administrator rights.
param([string]$RunnerDir = 'D:\dev\gitlab-runner')
$ErrorActionPreference = 'Stop'
$runnerExe = Join-Path $RunnerDir 'gitlab-runner.exe'
$runnerConfig = Join-Path $RunnerDir 'config.toml'
$tokenFile = Join-Path $RunnerDir 'runner-token.txt'
if (-not (Test-Path -LiteralPath $runnerExe)) { throw "GitLab Runner is missing: $runnerExe" }
$pwshDir = Split-Path (Get-Command pwsh -ErrorAction Stop).Source
$env:PATH = "$pwshDir;$env:PATH"
foreach ($folder in 'builds','cache','tmp','dotnet-home','nuget','npm-cache') {
    New-Item -ItemType Directory -Force -Path (Join-Path $RunnerDir $folder) | Out-Null
}
if (-not (Test-Path -LiteralPath $runnerConfig)) {
    if (-not (Test-Path -LiteralPath $tokenFile)) { throw "Save the project runner authentication token in $tokenFile" }
    $env:CI_SERVER_TOKEN = (Get-Content -LiteralPath $tokenFile -Raw).Trim()
    if ($env:CI_SERVER_TOKEN -notmatch '^glrt-\S+$') { $env:CI_SERVER_TOKEN = $null; throw 'Expected a glrt- runner authentication token.' }
    try {
        & $runnerExe register --non-interactive --url https://gitlab.com --executor shell --shell pwsh --name 'Sol Windows local' --config $runnerConfig --builds-dir "$RunnerDir\builds" --cache-dir "$RunnerDir\cache" --env "TEMP=$RunnerDir\tmp" --env "TMP=$RunnerDir\tmp" --env "DOTNET_CLI_HOME=$RunnerDir\dotnet-home" --env "NUGET_PACKAGES=$RunnerDir\nuget" --env "npm_config_cache=$RunnerDir\npm-cache" *> (Join-Path $RunnerDir 'registration.log')
        if ($LASTEXITCODE -ne 0) { throw 'Runner registration failed. Inspect registration.log locally (it may contain token details).' }
    }
    finally { $env:CI_SERVER_TOKEN = $null }
    $currentAccount = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
    & icacls $runnerConfig /inheritance:r /grant:r "${currentAccount}:(F)" 'SYSTEM:(F)' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not restrict access to runner credentials.' }
    Remove-Item -LiteralPath $tokenFile
}
$existingRunner = Get-CimInstance Win32_Process -Filter "Name='gitlab-runner.exe'" | Where-Object { $_.ExecutablePath -eq $runnerExe }
# A persistent shell runner reuses its checkout; compiler servers must release its SDK before Git cleans it.
$configText = Get-Content -LiteralPath $runnerConfig -Raw
$cleanup = '$prefix = [IO.Path]::GetFullPath($env:CI_PROJECT_DIR).TrimEnd("\") + "\"; Get-CimInstance Win32_Process | Where-Object { $_.Name -in "VBCSCompiler.exe","MSBuild.exe","dotnet.exe" -and $_.ExecutablePath -and $_.ExecutablePath.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }'
foreach ($hook in 'pre_get_sources_script','post_build_script') {
    $line = "$hook = " + (ConvertTo-Json -InputObject $cleanup -Compress)
    if ($configText -match "(?m)^\s*$hook\s*=") { $configText = [regex]::Replace($configText,"(?m)^\s*$hook\s*=.*$",[System.Text.RegularExpressions.MatchEvaluator]{param($m) $line}) }
    else { $configText = $configText.Replace('[[runners]]', "[[runners]]`n  $line") }
}
foreach ($setting in 'DOTNET_CLI_USE_MSBUILD_SERVER=0','MSBUILDDISABLENODEREUSE=1','UseSharedCompilation=false') {
    if (-not $configText.Contains($setting)) { $configText = [regex]::Replace($configText,'(?m)(environment\s*=\s*\[)',[System.Text.RegularExpressions.MatchEvaluator]{param($m) $m.Value + '"' + $setting + '", '}) }
}
Set-Content -LiteralPath $runnerConfig -Value $configText -Encoding utf8
if ($existingRunner) { Write-Output 'Runner is already running.'; return }
Start-Process -FilePath $runnerExe -ArgumentList @('run','--config',$runnerConfig,'--working-directory',$RunnerDir) -WorkingDirectory $RunnerDir -WindowStyle Hidden -RedirectStandardOutput (Join-Path $RunnerDir 'runner-output.log') -RedirectStandardError (Join-Path $RunnerDir 'runner-error.log')
Write-Output 'Runner started. It runs while this Windows session remains active.'
