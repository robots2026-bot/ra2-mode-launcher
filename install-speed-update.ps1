param(
    [string]$Runtime = 'D:\Software\RA2Mode',
    [string]$Package = (Join-Path $PSScriptRoot 'bin\Release\net8.0-windows\publish')
)
$ErrorActionPreference = 'Stop'
$Runtime = [IO.Path]::GetFullPath($Runtime)
$Package = [IO.Path]::GetFullPath($Package)
if (-not (Test-Path -LiteralPath (Join-Path $Runtime 'ra2.mix'))) { throw 'Game directory must contain ra2.mix.' }
foreach ($name in @('Ra2ModeLauncher.exe', 'Ra2ModeLauncher.dll', 'native-runtime\ddraw.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $Package $name))) { throw "Incomplete update: $name" }
}
foreach ($process in Get-Process -Name 'gamemd-spawn','gamemd','Ra2ModeLauncher' -ErrorAction SilentlyContinue) {
    throw "Close the game and launcher before updating (PID $($process.Id))."
}
$destination = Join-Path $Runtime 'RA2ModeLauncher'
$backup = Join-Path $destination ('backups\speed-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$entries = @()
foreach ($source in Get-ChildItem -LiteralPath $Package -File -Recurse) {
    $relative = $source.FullName.Substring($Package.Length).TrimStart('\')
    $entries += @{ Source = $source.FullName; Target = (Join-Path $destination $relative); Relative = ('launcher\' + $relative) }
}
$entries += @{ Source = (Join-Path $Package 'native-runtime\ddraw.dll'); Target = (Join-Path $Runtime 'ddraw.dll'); Relative = 'game\ddraw.dll' }
New-Item -ItemType Directory -Path $backup -Force | Out-Null
$written = @()
try {
    foreach ($entry in $entries) {
        $entry.Existed = Test-Path -LiteralPath $entry.Target
        $entry.Backup = Join-Path $backup $entry.Relative
        if ($entry.Existed) {
            New-Item -ItemType Directory -Path (Split-Path $entry.Backup) -Force | Out-Null
            Copy-Item -LiteralPath $entry.Target -Destination $entry.Backup
        }
        New-Item -ItemType Directory -Path (Split-Path $entry.Target) -Force | Out-Null
        $written += $entry
        Copy-Item -LiteralPath $entry.Source -Destination $entry.Target -Force
        if ((Get-FileHash -LiteralPath $entry.Source).Hash -ne (Get-FileHash -LiteralPath $entry.Target).Hash) { throw "Hash mismatch: $($entry.Target)" }
    }
    Write-Output "Speed update installed. Backup: $backup"
} catch {
    foreach ($entry in $written) {
        if ($entry.Existed) { Copy-Item -LiteralPath $entry.Backup -Destination $entry.Target -Force }
        elseif (Test-Path -LiteralPath $entry.Target) { Remove-Item -LiteralPath $entry.Target }
    }
    throw
}
