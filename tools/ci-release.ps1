# Publication par tag, ou lancement API explicite sur la branche principale avec SOL_RELEASE_VERSION.
# Le jeton natif du job GitLab suffit : aucune lecture d'identifiants du poste de développement.
$ErrorActionPreference = 'Stop'
$version = ([xml](Get-Content src/Browser.App/Browser.App.csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw 'Project version is missing' }
$tag = "v$version"
if ($env:CI_COMMIT_TAG) {
    if ($env:CI_COMMIT_TAG -ne $tag) { throw 'Tag does not match project version' }
} elseif ($env:CI_PIPELINE_SOURCE -ne 'api' -or $env:CI_COMMIT_BRANCH -ne $env:CI_DEFAULT_BRANCH -or $env:SOL_RELEASE_VERSION -ne $version) {
    throw 'Release requires a matching tag or explicit API version on the default branch'
}
if (-not $env:CI_JOB_TOKEN -or -not $env:CI_COMMIT_SHA -or -not $env:CI_PROJECT_ID -or -not $env:CI_API_V4_URL) { throw 'GitLab CI release context is incomplete' }
$headers = @{ 'JOB-TOKEN' = $env:CI_JOB_TOKEN }
$project = "$env:CI_API_V4_URL/projects/$env:CI_PROJECT_ID"
$existing = $null
try { $existing = Invoke-RestMethod -Headers $headers -Uri "$project/releases/$tag" }
catch { if ([int]$_.Exception.Response.StatusCode -ne 404) { throw } }
if ($existing) {
    if ($existing.commit.id -ne $env:CI_COMMIT_SHA -or @($existing.assets.links).Count -ne 13) {
        throw 'Existing release has a different commit or incomplete assets; refusing to overwrite it'
    }
    Write-Host "Release $tag already published from this commit with 13 assets."
    return
}

dotnet tool install -g vpk --version 1.2.161
if ($LASTEXITCODE -ne 0) { throw 'Velopack installation failed' }
pwsh -NoProfile -File tools/publish.ps1 -Arch x64,arm64,x86 -Out release
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
$files = @(Get-ChildItem release/*.zip, release/*-Setup.exe, release/SHA256SUMS.txt, release/installer/*.nupkg, release/installer/releases.*.json)
if ($files.Count -ne 13) { throw 'Expected exactly 13 release assets' }
$base = "$project/packages/generic/sol/$version"
./tools/ci-upload.ps1 -Files $files -BaseUri $base -Headers $headers
$links = @($files | ForEach-Object { @{ name = $_.Name; url = "$base/$($_.Name)"; link_type = 'package' } })
$notes = "docs/release-v$version.md"
$description = if (Test-Path $notes) { Get-Content $notes -Raw } else { "Sol $version" }
$body = @{ tag_name = $tag; ref = $env:CI_COMMIT_SHA; tag_message = "Sol $version";
    name = $tag; description = $description; assets = @{ links = $links } } | ConvertTo-Json -Depth 5
# L'API crée le tag annoté s'il n'existe pas, au commit exact testé et publié.
$published = Invoke-RestMethod -Method Post -Headers $headers -ContentType 'application/json; charset=utf-8' -Body $body -Uri "$project/releases"
if ($published.commit.id -ne $env:CI_COMMIT_SHA -or @($published.assets.links).Count -ne 13) { throw 'Published release verification failed' }
Write-Host "Published $tag with 13 assets from $env:CI_COMMIT_SHA"
