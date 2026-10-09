param([string]$SourcePath = (Join-Path $PSScriptRoot '..\..\vendor\cnc-ddraw-speed'))
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'prepare-source.ps1') -SourcePath $SourcePath
$sourceRoot = (Resolve-Path -LiteralPath $SourcePath).Path
$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$artifactRoot = Join-Path $workspaceRoot 'artifacts\cnc-speed'
[IO.Directory]::CreateDirectory($artifactRoot) | Out-Null
$vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$vs) { throw 'An existing MSVC x86 build environment is required; no installer is run.' }
$msbuild = Join-Path $vs 'MSBuild\Current\Bin\MSBuild.exe'
# Prevent upstream build events from copying to a debugger's game directory or
# deriving our component version from the unrelated parent launcher repository.
[xml]$project = [IO.File]::ReadAllText((Join-Path $sourceRoot 'cnc-ddraw.vcxproj'))
$ns = [Xml.XmlNamespaceManager]::new($project.NameTable)
$ns.AddNamespace('p', 'http://schemas.microsoft.com/developer/msbuild/2003')
foreach ($node in @($project.SelectNodes('//p:PreBuildEvent|//p:PostBuildEvent', $ns))) { $node.ParentNode.RemoveChild($node) | Out-Null }
foreach ($group in $project.SelectNodes('//p:ItemDefinitionGroup', $ns)) {
    $compile = $group.SelectSingleNode('p:ClCompile', $ns)
    if ($compile) { $node = $project.CreateElement('DebugInformationFormat', $project.DocumentElement.NamespaceURI); $node.InnerText = 'None'; $compile.AppendChild($node) | Out-Null }
    $link = $group.SelectSingleNode('p:Link', $ns)
    if ($link) { $node = $project.CreateElement('GenerateDebugInformation', $project.DocumentElement.NamespaceURI); $node.InnerText = 'false'; $link.AppendChild($node) | Out-Null }
}
$buildProject = Join-Path $sourceRoot 'cnc-ddraw.launcher.vcxproj'
$project.Save($buildProject)
[IO.File]::WriteAllText((Join-Path $sourceRoot 'inc\git.h'), "#define GIT_COMMIT `"39f1472-launcher-speed-v1`"`n#define GIT_BRANCH `"launcher-speed`"`n", [Text.UTF8Encoding]::new($false))
$buildArgs = @($buildProject, '/t:Build', '/m:2', '/v:minimal', '/p:Configuration=Release', '/p:Platform=Win32', '/p:PlatformToolset=v145', '/p:WholeProgramOptimization=false', '/p:LinkIncremental=false', '/p:WindowsTargetPlatformVersion=10.0.26100.0', "/p:OutDir=$artifactRoot\", "/p:IntDir=$sourceRoot\obj\launcher-speed\")
& $msbuild @buildArgs 2>&1 | Tee-Object -FilePath (Join-Path $artifactRoot 'build.log')
$buildExit = $LASTEXITCODE
if ($buildExit -ne 0) { throw "Native build failed: $buildExit" }
Copy-Item -LiteralPath (Join-Path $sourceRoot 'LICENSE') -Destination (Join-Path $artifactRoot 'LICENSE.cnc-ddraw.txt') -Force
Copy-Item -LiteralPath (Join-Path $sourceRoot 'src\detours\LICENSE.md') -Destination (Join-Path $artifactRoot 'LICENSE.Detours.txt') -Force
$hash = (Get-FileHash -LiteralPath (Join-Path $artifactRoot 'ddraw.dll') -Algorithm SHA256).Hash
Write-Output "Built native controller: $artifactRoot\ddraw.dll SHA256=$hash"
