param([Parameter(Mandatory = $true)][string]$SourcePath)
$ErrorActionPreference = 'Stop'
$pinnedCommit = '39f14721c998c937118615c1c4eb02ce20a4e638'
$sourceRoot = [IO.Path]::GetFullPath($SourcePath)
$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$vendorRoot = Join-Path $workspaceRoot 'vendor'
if (!$sourceRoot.StartsWith($vendorRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Source checkout must be inside workspace vendor directory.' }
$manifestPath = Join-Path $workspaceRoot 'vendor\cnc-ddraw-source-manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath)) {
    & (Join-Path $PSScriptRoot 'fetch-source.ps1')
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.commit -ne $pinnedCommit) { throw "Expected exact upstream commit $pinnedCommit" }
foreach ($entry in $manifest.files) {
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($entry.content)
    $header = [Text.Encoding]::ASCII.GetBytes("blob $($bytes.Length)`0")
    $hashInput = [byte[]]::new($header.Length + $bytes.Length)
    [Buffer]::BlockCopy($header,0,$hashInput,0,$header.Length)
    [Buffer]::BlockCopy($bytes,0,$hashInput,$header.Length,$bytes.Length)
    if ([Convert]::ToHexString([Security.Cryptography.SHA1]::HashData($hashInput)).ToLowerInvariant() -ne $entry.sha) { throw "Source manifest mismatch: $($entry.path)" }
    $destination = [IO.Path]::GetFullPath((Join-Path $sourceRoot $entry.path))
    if (!$destination.StartsWith($sourceRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid source path' }
    # Regenerate from verified originals; never rely on a previously patched checkout.
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
    [IO.File]::WriteAllBytes($destination, $bytes)
}
$wndPath = Join-Path $sourceRoot 'src\wndproc.c'
$wnd = [IO.File]::ReadAllText($wndPath)
$include = '#include "launcher_speed.h"'
if (!$wnd.Contains($include)) {
    $anchor = '#include "versionhelpers.h"'
    if (($wnd.Split(@($anchor), [StringSplitOptions]::None)).Length -ne 2) { throw 'Unexpected wndproc include anchor' }
    $entry = "LRESULT CALLBACK fake_WndProc(HWND hWnd, UINT uMsg, WPARAM wParam, LPARAM lParam)`n{"
    $normalized = $wnd.Replace("`r`n", "`n")
    if (($normalized.Split(@($entry), [StringSplitOptions]::None)).Length -ne 2) { throw 'Unexpected wndproc entry anchor' }
    $normalized = $normalized.Replace($anchor, $anchor + "`n#include <string.h>`n" + $include)
    $normalized = $normalized.Replace($entry, $entry + "`n    if (uMsg && uMsg == launcher_speed_message()) return launcher_speed_control(wParam, lParam);")
    [IO.File]::WriteAllText($wndPath, $normalized, [Text.UTF8Encoding]::new($false))
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'launcher_speed.h') -Destination (Join-Path $sourceRoot 'inc\launcher_speed.h') -Force
$ddPath = Join-Path $sourceRoot 'src\dd.c'
$dd = [IO.File]::ReadAllText($ddPath)
$includeAnchor = '#include "versionhelpers.h"'
if (!$dd.Contains($includeAnchor)) { throw 'Missing DirectDraw initialization include anchor' }
$dd = $dd.Replace($includeAnchor, $includeAnchor + "`n#include <string.h>`n#include `"launcher_speed.h`"")
$flipAnchor = 'if (g_config.maxgameticks >= 0 || g_config.maxgameticks == -2)'
if (!$dd.Contains($flipAnchor)) { throw 'Missing legacy flip limiter anchor' }
$dd = $dd.Replace($flipAnchor, 'if ((g_config.maxgameticks >= 0 || g_config.maxgameticks == -2) && !launcher_speed_managed_launch())')
[IO.File]::WriteAllText($ddPath, $dd, [Text.UTF8Encoding]::new($false))
Write-Output "Prepared pinned source: $sourceRoot"
Write-Output 'No binary built or deployed. Build upstream Release|Win32 and retain its LICENSE with the DLL.'
