$ErrorActionPreference = 'Stop'
$version = $env:SOL_RELEASE_VERSION
$source = $env:SOL_SOURCE_COMMIT
$repo = 'stvekoulo/sol-browser'
if ($version -notmatch '^\d+\.\d+\.\d+$' -or $source -notmatch '^[a-f0-9]{40}$') {
    throw 'An exact version and source commit are required.'
}
if ($env:GITHUB_REPOSITORY -ne $repo -or $env:GITHUB_REF -ne 'refs/heads/main') {
    throw 'Only a manually dispatched release from the trusted main branch is supported.'
}
$tag = "v$version"
$tagCommit = git rev-parse "$tag^{commit}"
if ($LASTEXITCODE -or $tagCommit -ne $source) { throw 'The tag does not match the GitLab source commit.' }
$projectVersion = ([xml](Get-Content src/Browser.App/Browser.App.csproj)).Project.PropertyGroup.Version |
    Where-Object { $_ } | Select-Object -First 1
if ($projectVersion -ne $version) { throw 'Project and release versions differ.' }
$allowed = @('.github/workflows/release-local.yml', 'tools/github-release.ps1')
$changed = @(git diff --name-only $source HEAD)
if ($LASTEXITCODE -or @($changed | Where-Object { $_ -notin $allowed }).Count) {
    throw 'Product files differ from the declared GitLab source.'
}
$gh = if (Test-Path 'C:/Program Files/GitHub CLI/gh.exe') {
    'C:/Program Files/GitHub CLI/gh.exe'
} else { (Get-Command gh -ErrorAction Stop).Source }
function Invoke-GitHub([string[]]$Arguments) {
    $result = & $gh @Arguments
    if ($LASTEXITCODE) { throw "GitHub command failed: $($Arguments[0])" }
    return $result
}
$existing = @(Invoke-GitHub -Arguments @('api', "repos/$repo/releases?per_page=100")) -join "`n" |
    ConvertFrom-Json | Where-Object { $_.tag_name -eq $tag }
if ($existing -and -not $existing.draft) {
    throw 'This version is already public; refusing to replace its assets.'
}
. ./tools/ci-setup.ps1
Push-Location ui
try { npm.cmd ci; if ($LASTEXITCODE) { throw 'npm ci failed.' } }
finally { Pop-Location }
$out = Join-Path $env:RUNNER_TEMP "sol-$version-$env:GITHUB_RUN_ID-$env:GITHUB_RUN_ATTEMPT"
if (Test-Path $out) { throw 'Release output must be a new dedicated directory.' }
./tools/publish.ps1 -Arch x64,arm64,x86 -Out $out
if ($LASTEXITCODE) { throw 'Windows build failed.' }
$files = @(Get-ChildItem "$out/*.zip", "$out/*-Setup.exe", "$out/SHA256SUMS.txt",
    "$out/installer/*.nupkg", "$out/installer/releases.*.json")
if ($files.Count -ne 13) { throw 'Exactly 13 release assets are required.' }
foreach ($arch in 'x64', 'arm64', 'x86') {
    $index = Get-Content "$out/installer/releases.win-$arch.json" -Raw | ConvertFrom-Json
    if ($index.Assets.Count -ne 1) { throw "Unexpected update index for $arch." }
    $asset = $index.Assets[0]
    $package = Join-Path "$out/installer" $asset.FileName
    if ($asset.Version -ne $version -or -not (Test-Path $package) -or
        (Get-Item $package).Length -ne $asset.Size -or
        (Get-FileHash $package -Algorithm SHA1).Hash -ne $asset.SHA1) {
        throw "Invalid update package for $arch."
    }
}
$sums = Get-Content "$out/SHA256SUMS.txt"
if ($sums.Count -ne 6) { throw 'Six installer/archive checksums are required.' }
foreach ($line in $sums) {
    if ($line -notmatch '^([a-f0-9]{64})\s+(.+)$' -or
        (Get-FileHash (Join-Path $out $Matches[2]) -Algorithm SHA256).Hash -ne $Matches[1]) {
        throw 'A release checksum is invalid.'
    }
}
if (-not $existing) {
    $notes = "docs/release-v$version.md"
    Invoke-GitHub -Arguments @('release', 'create', $tag, '--repo', $repo, '--verify-tag', '--draft',
        '--title', $tag, '--notes-file', $notes) | Out-Host
}
Invoke-GitHub -Arguments (@('release', 'upload', $tag, '--repo', $repo, '--clobber') + @($files.FullName)) | Out-Host
$release = Invoke-GitHub -Arguments @('api', "repos/$repo/releases?per_page=100") |
    ConvertFrom-Json | Where-Object { $_.tag_name -eq $tag }
if (-not $release.draft -or $release.assets.Count -ne 13) { throw 'Incomplete draft release.' }
$evidence = foreach ($file in $files) {
    $asset = @($release.assets | Where-Object { $_.name -eq $file.Name })
    $sha256 = (Get-FileHash $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($asset.Count -ne 1 -or $asset[0].size -ne $file.Length -or
        ($asset[0].digest -and $asset[0].digest -ne "sha256:$sha256")) {
        throw "Uploaded asset verification failed: $($file.Name)"
    }
    [pscustomobject]@{ name = $file.Name; size = $file.Length; sha256 = $sha256 }
}
$evidence | ConvertTo-Json | Set-Content "$out/github-verification.json"
Invoke-GitHub -Arguments @('release', 'edit', $tag, '--repo', $repo, '--draft=false', '--latest') | Out-Host
$published = Invoke-GitHub -Arguments @('api', "repos/$repo/releases/tags/$tag") | ConvertFrom-Json
if ($published.draft -or $published.assets.Count -ne 13) { throw 'Public release verification failed.' }
Write-Host "Published $tag from GitLab source $source with 13 verified assets: $($published.html_url)"
