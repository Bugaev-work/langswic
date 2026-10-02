param(
    [Parameter(Mandatory=$true)][ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+$')][string]$Version,
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')][string]$Repository = 'Bugaev-work/langswic',
    [switch]$Publish
)
$ErrorActionPreference = 'Stop'
# Maintainer tool. The installed application never uses GitHub authentication.
function ProjectGit {
    $result = & git -c "safe.directory=$PSScriptRoot" -C $PSScriptRoot @args
    if ($LASTEXITCODE -ne 0) { throw 'Git command failed.' }
    return $result
}
if (ProjectGit status --porcelain) { throw 'Commit all project changes before preparing a release.' }
$origin = ProjectGit remote get-url origin
if ($origin -ne "https://github.com/$Repository.git") { throw 'Repository does not match the configured origin.' }
$source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Program.cs'))
if (!$source.Contains('key.SetValue("DisplayVersion","' + $Version + '")')) { throw 'Version differs from installer source metadata.' }
$commit = ProjectGit rev-parse HEAD
$branch = ProjectGit branch --show-current
$remote = ProjectGit ls-remote origin "refs/heads/$branch"
if (!$branch -or !$remote -or (($remote -split '\s+')[0] -ne $commit)) { throw 'Push the current commit before preparing a release.' }
$out = Join-Path $PSScriptRoot 'dist'
$package = Join-Path $out 'langswic-Setup.exe'
$sums = Join-Path $out 'SHA256SUMS.txt'
$expected = (([IO.File]::ReadAllText($sums)).Trim() -split '\s+')[0]
if ($expected -notmatch '^[A-Fa-f0-9]{64}$' -or (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ne $expected) { throw 'Installer checksum mismatch.' }
$notes = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'RELEASE_NOTES.md'))
if (!$notes.StartsWith('# langswic ' + $Version + ' ')) { throw 'Release notes do not match the version.' }
$tag = 'v' + $Version
$api = 'https://api.github.com/repos/' + $Repository
$env:GCM_INTERACTIVE = 'never'
$credentialLines = "protocol=https`nhost=github.com`n`n" | & git -c "safe.directory=$PSScriptRoot" -C $PSScriptRoot credential fill
if ($LASTEXITCODE -ne 0) { throw 'GitHub authentication unavailable.' }
$credential = @{}
foreach ($line in $credentialLines) { $pair = $line -split '=',2; if ($pair.Count -eq 2) { $credential[$pair[0]] = $pair[1] } }
if (!$credential['password']) { throw 'No GitHub credential returned.' }
$headers = @{ Authorization = 'Bearer ' + $credential['password']; Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2022-11-28'; 'User-Agent' = 'langswic-release' }
try {
    $repo = Invoke-RestMethod -Uri $api -Headers $headers
    if ($Publish -and $repo.private) { throw 'Anonymous installation requires a public repository. Change visibility separately with owner authorization.' }
    $release = $null
    try { $release = Invoke-RestMethod -Uri ($api + '/releases/tags/' + $tag) -Headers $headers }
    catch { if (!$_.Exception.Response -or [int]$_.Exception.Response.StatusCode -ne 404) { throw } }
    if (!$release) {
        # The tag endpoint finds published releases; drafts are found in the list.
        for ($page = 1; !$release; $page++) {
            $items = @(Invoke-RestMethod -Uri ($api + '/releases?per_page=100&page=' + $page) -Headers $headers)
            $matches = @($items | Where-Object { $_.tag_name -eq $tag })
            if ($matches.Count -gt 1) { throw 'Multiple releases use this tag; resolve manually.' }
            if ($matches.Count) { $release = $matches[0] }
            if ($items.Count -lt 100) { break }
        }
    }
    if ($release -and !$release.draft) { throw 'This version is already published; published release files are not replaced.' }
    $body = @{ tag_name = $tag; target_commitish = $commit; name = "langswic $Version"; body = $notes; draft = $true; prerelease = $false } | ConvertTo-Json -Compress
    $bodyBytes = [Text.Encoding]::UTF8.GetBytes($body)
    if (!$release) { $release = Invoke-RestMethod -Uri ($api + '/releases') -Method Post -Headers $headers -Body $bodyBytes -ContentType 'application/json; charset=utf-8' }
    else { $release = Invoke-RestMethod -Uri ($api + '/releases/' + $release.id) -Method Patch -Headers $headers -Body $bodyBytes -ContentType 'application/json; charset=utf-8' }
    $assets = @(
        @{ Path = $package; Name = 'langswic-Setup.exe' },
        @{ Path = $sums; Name = 'SHA256SUMS.txt' },
        @{ Path = (Join-Path $PSScriptRoot 'INSTALL.md'); Name = 'INSTALL.md' }
    )
    foreach ($asset in $assets) {
        $digest = 'sha256:' + (Get-FileHash -LiteralPath $asset.Path -Algorithm SHA256).Hash.ToLowerInvariant()
        $existing = @($release.assets | Where-Object { $_.name -eq $asset.Name })
        if ($existing.Count) {
            if ($existing[0].digest -ne $digest) { throw ('Draft asset differs: ' + $asset.Name) }
            continue
        }
        $upload = 'https://uploads.github.com/repos/' + $Repository + '/releases/' + $release.id + '/assets?name=' + [Uri]::EscapeDataString($asset.Name)
        $uploaded = Invoke-RestMethod -Uri $upload -Method Post -Headers $headers -InFile $asset.Path -ContentType 'application/octet-stream'
        if ($uploaded.digest -ne $digest -or $uploaded.state -ne 'uploaded') { throw ('Uploaded checksum verification failed: ' + $asset.Name) }
    }
    if ($Publish) {
        $publishBody = [Text.Encoding]::UTF8.GetBytes((@{ draft = $false; make_latest = 'true' } | ConvertTo-Json -Compress))
        $release = Invoke-RestMethod -Uri ($api + '/releases/' + $release.id) -Method Patch -Headers $headers -Body $publishBody -ContentType 'application/json; charset=utf-8'
    } else { $release = Invoke-RestMethod -Uri ($api + '/releases/' + $release.id) -Headers $headers }
    if (@($release.assets).Count -lt 3) { throw 'Release assets incomplete.' }
    [pscustomobject]@{ Release = $release.html_url; Draft = $release.draft; Tag = $release.tag_name; Commit = $commit; Assets = @($release.assets.name); SHA256 = $expected } | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $out ('release-' + $Version + '.json')) -Encoding UTF8
    Write-Output ('Release: ' + $release.html_url)
    Write-Output ('Draft: ' + $release.draft)
    $release.assets | Select-Object name,size,digest | Format-Table -AutoSize
} catch { throw ('Release preparation failed: ' + $_.Exception.Message) }
finally { $headers.Clear(); $credential.Clear(); $credentialLines = $null }
