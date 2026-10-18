param(
    [Parameter(Mandatory=$true)][string]$BaseEmulator,
    [Parameter(Mandatory=$true)][string]$Zig,
    [Parameter(Mandatory=$true)][string]$Python,
    [string]$Out = (Join-Path $PSScriptRoot '..\build')
)
$ErrorActionPreference = 'Stop'
$nhlBuildOutput = [System.IO.Path]::GetFullPath($Out)
New-Item -ItemType Directory -Path $nhlBuildOutput -Force | Out-Null
& $Zig cc -target x86_64-windows-gnu -O2 -shared (Join-Path $PSScriptRoot 'shader_fix.c') -o (Join-Path $nhlBuildOutput 'nhl_shader_fix.dll')
if ($LASTEXITCODE -ne 0) { throw 'Shader module compilation failed' }
& $Python (Join-Path $PSScriptRoot 'patch_emulator.py') --emulator $BaseEmulator --output (Join-Path $nhlBuildOutput 'nhl24.exe') --manifest (Join-Path $nhlBuildOutput 'emulator-patch-manifest.json')
if ($LASTEXITCODE -ne 0) { throw 'Emulator adaptation failed' }
& $Zig cc -target x86_64-windows-gnu -O2 -Wall -Wextra -shared (Join-Path $PSScriptRoot 'arena_memory_fix.c') -o (Join-Path $nhlBuildOutput 'nhl_memory_fix.dll')
if ($LASTEXITCODE -ne 0) { throw 'Memory module compilation failed' }
& $Python (Join-Path $PSScriptRoot 'patch_arena_memory.py') --emulator (Join-Path $nhlBuildOutput 'nhl24.exe') --output (Join-Path $nhlBuildOutput 'nhl24.exe') --manifest (Join-Path $nhlBuildOutput 'memory-patch-manifest.json')
if ($LASTEXITCODE -ne 0) { throw 'Memory integration failed' }
& $Python (Join-Path $PSScriptRoot 'patch_readback.py') --emulator (Join-Path $nhlBuildOutput 'nhl24.exe') --output (Join-Path $nhlBuildOutput 'nhl24.exe') --manifest (Join-Path $nhlBuildOutput 'readback-patch-manifest.json')
if ($LASTEXITCODE -ne 0) { throw 'Readback integration failed' }
& $Python (Join-Path $PSScriptRoot 'patch_texture_overlap.py') --emulator (Join-Path $nhlBuildOutput 'nhl24.exe') --output (Join-Path $nhlBuildOutput 'nhl24.exe') --manifest (Join-Path $nhlBuildOutput 'texture-patch-manifest.json')
if ($LASTEXITCODE -ne 0) { throw 'Texture overlap integration failed' }
$nhlLauncherSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'NHL24Launcher.cs') -Raw
$nhlLauncherSource = $nhlLauncherSource.Replace('6a853e1145430e343a3801e071e1dd740df6795b181a540a1c6108e0b8268864', (Get-FileHash -LiteralPath (Join-Path $nhlBuildOutput 'nhl24.exe')).Hash.ToLowerInvariant())
$nhlLauncherSource = $nhlLauncherSource.Replace('7a3320c7a645bb9a80a2ca126bf29c54d860dd795db118154a1ef55807941932', (Get-FileHash -LiteralPath (Join-Path $nhlBuildOutput 'nhl_shader_fix.dll')).Hash.ToLowerInvariant())
$nhlLauncherSource = $nhlLauncherSource.Replace('c9d2f824234b9f2ed7d97b1b5e98e57b8a676b0eef60e575dbe1cc29c41b1160', (Get-FileHash -LiteralPath (Join-Path $nhlBuildOutput 'nhl_memory_fix.dll')).Hash.ToLowerInvariant())
$nhlGeneratedSource = Join-Path $nhlBuildOutput 'NHL24Launcher.build.cs'
[System.IO.File]::WriteAllText($nhlGeneratedSource, $nhlLauncherSource, [System.Text.UTF8Encoding]::new($false))
$nhlCSharp = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $nhlCSharp /nologo /target:winexe /optimize+ "/out:$(Join-Path $nhlBuildOutput 'NHL24Launcher.exe')" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll $nhlGeneratedSource
if ($LASTEXITCODE -ne 0) { throw 'Launcher compilation failed' }
Get-ChildItem -LiteralPath (Split-Path -Parent $BaseEmulator) -Filter '*.dll' -File | Copy-Item -Destination $nhlBuildOutput
