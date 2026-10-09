$ErrorActionPreference = 'Stop'
$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$artifactRoot = Join-Path $workspaceRoot 'artifacts\cnc-speed'
$fixtureRoot = Join-Path $artifactRoot 'timing-fixture'
[IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
$msvcRoot = 'C:\Program Files\Microsoft Visual Studio\18\Community\VC\Tools\MSVC\14.51.36231'
$sdkRoot = 'C:\Program Files (x86)\Windows Kits\10'
$sdkVersion = '10.0.26100.0'
$compiler = Join-Path $msvcRoot 'bin\Hostx64\x86\cl.exe'
$includePaths = @((Join-Path $msvcRoot 'include'), (Join-Path $sdkRoot "Include\$sdkVersion\ucrt"), (Join-Path $sdkRoot "Include\$sdkVersion\shared"), (Join-Path $sdkRoot "Include\$sdkVersion\um"), (Join-Path $workspaceRoot 'vendor\cnc-ddraw-speed\inc'))
$libPaths = @((Join-Path $msvcRoot 'lib\x86'), (Join-Path $sdkRoot "Lib\$sdkVersion\ucrt\x86"), (Join-Path $sdkRoot "Lib\$sdkVersion\um\x86"))
$compileArgs = @('/nologo', '/TC', '/W3', '/MT', '/O2', '/D_CRT_SECURE_NO_WARNINGS') + @($includePaths | ForEach-Object { "/I$_" }) + @((Join-Path $PSScriptRoot 'timing-smoke.c'), "/Fe$fixtureRoot\gamemd-spawn.exe", "/Fo$fixtureRoot\timing-smoke.obj", '/link') + @($libPaths | ForEach-Object { "/LIBPATH:$_" }) + @('user32.lib', 'gdi32.lib')
& $compiler @compileArgs 2>&1 | Tee-Object -FilePath (Join-Path $artifactRoot 'timing-build.log')
if ($LASTEXITCODE -ne 0) { throw "Timing fixture build failed: $LASTEXITCODE" }
Copy-Item -LiteralPath (Join-Path $artifactRoot 'ddraw.dll') -Destination (Join-Path $fixtureRoot 'ddraw.dll') -Force
[IO.File]::WriteAllText((Join-Path $fixtureRoot 'ddraw.ini'), "[ddraw]`nrenderer=gdi`nwindowed=true`nsavesettings=0`nmaxgameticks=60`nlimiter_type=1`nhook=0`n[gamemd-spawn]`nmaxgameticks=60`nlimiter_type=1`n", [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $fixtureRoot 'spawn.ini'), "[Settings]`nLauncherCncPacing=true`nLauncherLiveSpeed=true`nForceMultiplayer=false`n", [Text.UTF8Encoding]::new($false))
Push-Location -LiteralPath $fixtureRoot
try {
    & '.\gamemd-spawn.exe' 2>&1 | Tee-Object -FilePath (Join-Path $artifactRoot 'timing.log')
    if ($LASTEXITCODE -ne 0) { throw "Native timing check failed: $LASTEXITCODE" }
} finally { Pop-Location }
