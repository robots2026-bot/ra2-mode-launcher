param(
    [string]$Runtime = 'D:\Software\RA2Mode',
    [string]$OfficialPackage = (Join-Path $PSScriptRoot '..\vendor\package_9.3.3\package')
)

$ErrorActionPreference = 'Stop'
$OfficialPackage = [IO.Path]::GetFullPath($OfficialPackage)
$Runtime = [IO.Path]::GetFullPath($Runtime)

if (-not (Test-Path -LiteralPath (Join-Path $Runtime 'ra2.mix'))) { throw "当前游戏目录无效：$Runtime" }
if (-not (Test-Path -LiteralPath (Join-Path $OfficialPackage 'CnCNet-Spawner.dll'))) { throw "官方运行包不完整：$OfficialPackage" }

# Refresh the self-contained runtime from the bundled official CnCNet package.
# The launcher and this maintenance script never read from the old installation.
Copy-Item -Path (Join-Path $OfficialPackage '*') -Destination $Runtime -Recurse -Force

$exe = Join-Path $Runtime 'gamemd-spawn.exe'
$backup = Join-Path $Runtime 'gamemd-spawn.exe.official-9.3.3'
if (-not (Test-Path -LiteralPath $backup)) { Copy-Item -LiteralPath $exe -Destination $backup }

$image = [IO.File]::ReadAllBytes($exe)
$imageBase = 0x00400000
$hookVa = 0x00519996
$caveVa = 0x007DF781
$continueVa = 0x0051999E
$invalidDestinationVa = 0x00519B17
$invalidVirtualTarget = 0x007FB178
$hookOffset = $hookVa - $imageBase
$caveOffset = $caveVa - $imageBase

function Assert-Bytes([int]$Offset, [byte[]]$Expected) {
    for ($i = 0; $i -lt $Expected.Length; $i++) {
        if ($image[$Offset + $i] -ne $Expected[$i]) { throw "gamemd-spawn.exe 与已验证版本不匹配，偏移 0x$(($Offset + $i).ToString('X'))。" }
    }
}

function Relative-Jump([int]$From, [int]$To) {
    return [byte[]](0xE9) + [BitConverter]::GetBytes([int]($To - ($From + 5)))
}

$originalHook = [byte[]](0x8B, 0x11, 0xFF, 0x92, 0x80, 0x00, 0x00, 0x00)
Assert-Bytes $hookOffset $originalHook
Assert-Bytes $caveOffset ([byte[]](0xCC) * 30)

$hookPatch = (Relative-Jump $hookVa $caveVa) + [byte[]](0x90, 0x90, 0x90)
$cavePatch = [byte[]](0x8B, 0x11, 0x81, 0xBA, 0x80, 0x00, 0x00, 0x00) + [BitConverter]::GetBytes([int]$invalidVirtualTarget) + [byte[]](0x74, 0x0B, 0xFF, 0x92, 0x80, 0x00, 0x00, 0x00) + (Relative-Jump ($caveVa + 20) $continueVa) + (Relative-Jump ($caveVa + 25) $invalidDestinationVa)
[Array]::Copy($hookPatch, 0, $image, $hookOffset, $hookPatch.Length)
[Array]::Copy($cavePatch, 0, $image, $caveOffset, $cavePatch.Length)
[IO.File]::WriteAllBytes($exe, $image)

$launcherOutput = Join-Path $PSScriptRoot 'bin\Release\net8.0-windows\publish'
if (-not (Test-Path -LiteralPath (Join-Path $launcherOutput 'Ra2ModeLauncher.exe'))) { throw '尚未发布启动器，请先运行 dotnet publish。' }
$launcherDestination = Join-Path $Runtime 'RA2ModeLauncher'
New-Item -ItemType Directory -Path $launcherDestination -Force | Out-Null
Copy-Item -Path (Join-Path $launcherOutput '*') -Destination $launcherDestination -Recurse -Force

[pscustomobject]@{
    Runtime = $Runtime
    Launcher = (Join-Path $Runtime 'RA2ModeLauncher\Ra2ModeLauncher.exe')
    SpawnerSha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
    OriginalSpawnerBackup = $backup
}
