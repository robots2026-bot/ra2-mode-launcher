$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$metadata = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'source-manifest.json') -Raw | ConvertFrom-Json
$vendor = Join-Path $workspace 'vendor'
$archive = Join-Path $vendor ('cnc-ddraw-' + $metadata.commit + '.zip')
$extract = Join-Path $vendor ('cnc-ddraw-original-' + $metadata.commit)
New-Item -ItemType Directory -Path $vendor -Force | Out-Null
Invoke-WebRequest -Uri ("https://codeload.github.com/FunkyFr3sh/cnc-ddraw/zip/" + $metadata.commit) -OutFile $archive
Expand-Archive -LiteralPath $archive -DestinationPath $extract -Force
$root = Join-Path $extract ('cnc-ddraw-' + $metadata.commit)
$entries = @()
foreach ($entry in $metadata.files) {
    $bytes = [IO.File]::ReadAllBytes((Join-Path $root $entry.path))
    $header = [Text.Encoding]::ASCII.GetBytes("blob $($bytes.Length)`0")
    $blob = [byte[]]::new($header.Length + $bytes.Length)
    [Buffer]::BlockCopy($header, 0, $blob, 0, $header.Length)
    [Buffer]::BlockCopy($bytes, 0, $blob, $header.Length, $bytes.Length)
    $hash = [Convert]::ToHexString([Security.Cryptography.SHA1]::HashData($blob)).ToLowerInvariant()
    if ($hash -ne $entry.sha) { throw "Upstream source mismatch: $($entry.path)" }
    $entries += @{ path = $entry.path; sha = $hash; content = [Text.UTF8Encoding]::new($false, $true).GetString($bytes) }
}
@{ commit = $metadata.commit; files = $entries } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $vendor 'cnc-ddraw-source-manifest.json') -Encoding utf8
New-Item -ItemType Directory -Path (Join-Path $vendor 'cnc-ddraw-speed') -Force | Out-Null
Write-Output 'Pinned upstream source downloaded and verified; third-party source remains in ignored vendor/.'
