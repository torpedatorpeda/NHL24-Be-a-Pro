param(
    [Parameter(Mandatory=$true)][string]$BaseEmulator,
    [Parameter(Mandatory=$true)][string]$Zig,
    [Parameter(Mandatory=$true)][string]$Python,
    [string]$Out = (Join-Path $PSScriptRoot '..\build'),
    [switch]$ExperimentalPro
)
$ErrorActionPreference = 'Stop'
$nhlBuildOutput = [System.IO.Path]::GetFullPath($Out)
if (Test-Path -LiteralPath $nhlBuildOutput) { throw 'Use a new output directory so personal or stale files cannot enter the package' }
$nhlRuntimeOutput = Join-Path $nhlBuildOutput 'runtime'
New-Item -ItemType Directory -Path $nhlRuntimeOutput -Force | Out-Null
& $Zig cc -target x86_64-windows-gnu -O2 -shared (Join-Path $PSScriptRoot 'shader_fix.c') -o (Join-Path $nhlRuntimeOutput 'nhl_shader_fix.dll')
if ($LASTEXITCODE -ne 0) { throw 'Shader module compilation failed' }
& $Python (Join-Path $PSScriptRoot 'patch_emulator.py') --emulator $BaseEmulator --output (Join-Path $nhlRuntimeOutput 'nhl24.exe') --manifest (Join-Path $nhlBuildOutput 'emulator-patch-manifest.json')
if ($LASTEXITCODE -ne 0) { throw 'Emulator adaptation failed' }
& $Zig cc -target x86_64-windows-gnu -O2 -Wall -Wextra -shared (Join-Path $PSScriptRoot 'arena_memory_fix.c') -o (Join-Path $nhlRuntimeOutput 'nhl_memory_fix.dll')
if ($LASTEXITCODE -ne 0) { throw 'Memory module compilation failed' }
& $Python (Join-Path $PSScriptRoot 'patch_arena_memory.py') --emulator (Join-Path $nhlRuntimeOutput 'nhl24.exe') --output (Join-Path $nhlRuntimeOutput 'nhl24.exe') --manifest (Join-Path $nhlBuildOutput 'memory-patch-manifest.json')
if ($LASTEXITCODE -ne 0) { throw 'Memory integration failed' }
& $Python (Join-Path $PSScriptRoot 'patch_readback.py') --emulator (Join-Path $nhlRuntimeOutput 'nhl24.exe') --output (Join-Path $nhlRuntimeOutput 'nhl24.exe') --manifest (Join-Path $nhlBuildOutput 'readback-patch-manifest.json')
if ($LASTEXITCODE -ne 0) { throw 'Readback integration failed' }
& $Python (Join-Path $PSScriptRoot 'patch_texture_overlap.py') --emulator (Join-Path $nhlRuntimeOutput 'nhl24.exe') --output (Join-Path $nhlRuntimeOutput 'nhl24.exe') --manifest (Join-Path $nhlBuildOutput 'texture-patch-manifest.json')
if ($LASTEXITCODE -ne 0) { throw 'Texture overlap integration failed' }
& $Python (Join-Path $PSScriptRoot 'patch_mip_alias.py') --emulator (Join-Path $nhlRuntimeOutput 'nhl24.exe') --output (Join-Path $nhlRuntimeOutput 'nhl24.exe') --manifest (Join-Path $nhlBuildOutput 'mip-alias-manifest.json')
if ($LASTEXITCODE -ne 0) { throw 'Mip alias integration failed' }
& $Python (Join-Path $PSScriptRoot 'patch_texel_alias.py') --emulator (Join-Path $nhlRuntimeOutput 'nhl24.exe') --output (Join-Path $nhlRuntimeOutput 'nhl24.exe') --manifest (Join-Path $nhlBuildOutput 'texel-alias-manifest.json')
if ($LASTEXITCODE -ne 0) { throw 'Texel alias integration failed' }
& $Python (Join-Path $PSScriptRoot 'patch_graphics.py') --emulator (Join-Path $nhlRuntimeOutput 'nhl24.exe') --output (Join-Path $nhlRuntimeOutput 'nhl24.exe') --manifest (Join-Path $nhlBuildOutput 'graphics-manifest.json')
if ($LASTEXITCODE -ne 0) { throw 'Graphics integration failed' }
if ($ExperimentalPro) {
& $Zig cc -target x86_64-windows-gnu -O2 -Wall -Wextra -shared (Join-Path $PSScriptRoot 'pro_shader_fix.c') -o (Join-Path $nhlRuntimeOutput 'nhl_pro_fix.dll')
if ($LASTEXITCODE -ne 0) { throw 'Pro shader module compilation failed' }
& $Python (Join-Path $PSScriptRoot 'patch_pro.py') --emulator (Join-Path $nhlRuntimeOutput 'nhl24.exe') --output (Join-Path $nhlRuntimeOutput 'nhl24.exe') --manifest (Join-Path $nhlBuildOutput 'pro-manifest.json')
if ($LASTEXITCODE -ne 0) { throw 'Pro shader integration failed' }
} else {
    & $Python (Join-Path $PSScriptRoot 'patch_image_depth.py') --emulator (Join-Path $nhlRuntimeOutput 'nhl24.exe') --output (Join-Path $nhlRuntimeOutput 'nhl24.exe') --manifest (Join-Path $nhlBuildOutput 'image-depth-manifest.json')
    if ($LASTEXITCODE -ne 0) { throw 'Image descriptor integration failed' }
    & $Zig cc -target x86_64-windows-gnu -O2 -Wall -Wextra -shared (Join-Path $PSScriptRoot 'device_safety.c') -o (Join-Path $nhlRuntimeOutput 'nhl_device_fix.dll')
    if ($LASTEXITCODE -ne 0) { throw 'GPU safety module compilation failed' }
    & $Python (Join-Path $PSScriptRoot 'patch_device_safety.py') --emulator (Join-Path $nhlRuntimeOutput 'nhl24.exe') --output (Join-Path $nhlRuntimeOutput 'nhl24.exe') --manifest (Join-Path $nhlBuildOutput 'device-safety-manifest.json')
    if ($LASTEXITCODE -ne 0) { throw 'GPU safety integration failed' }
}
$nhlLauncherSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'NHL24Launcher.cs') -Raw
$nhlIntegrityFiles = @('nhl24.exe', 'nhl_shader_fix.dll', 'nhl_memory_fix.dll')
if ($ExperimentalPro) { $nhlIntegrityFiles += 'nhl_pro_fix.dll' }
else { $nhlIntegrityFiles += 'nhl_device_fix.dll' }
foreach ($nhlIntegrityFile in $nhlIntegrityFiles) {
    $nhlIntegrityHash = (Get-FileHash -LiteralPath (Join-Path $nhlRuntimeOutput $nhlIntegrityFile)).Hash.ToLowerInvariant()
    $nhlIntegrityPattern = '(\{\s*"' + [regex]::Escape($nhlIntegrityFile) + '",\s*")[a-f0-9]{64}("\s*\})'
    if ([regex]::Matches($nhlLauncherSource, $nhlIntegrityPattern).Count -ne 1) { throw "Missing integrity entry: $nhlIntegrityFile" }
    $nhlLauncherSource = [regex]::Replace($nhlLauncherSource, $nhlIntegrityPattern, [System.Text.RegularExpressions.MatchEvaluator]{ param($match) $match.Groups[1].Value + $nhlIntegrityHash + $match.Groups[2].Value })
}
$nhlGeneratedSource = Join-Path $nhlBuildOutput 'NHL24Launcher.build.cs'
[System.IO.File]::WriteAllText($nhlGeneratedSource, $nhlLauncherSource, [System.Text.UTF8Encoding]::new($false))
$nhlCSharp = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$nhlBuildDefine = if ($ExperimentalPro) { '/define:EXPERIMENTAL_PRO' } else { '/define:STANDARD_BUILD' }
& $nhlCSharp /nologo /target:winexe /optimize+ $nhlBuildDefine "/out:$(Join-Path $nhlBuildOutput 'NHL24Launcher.exe')" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll $nhlGeneratedSource (Join-Path $PSScriptRoot 'LauncherData.cs')
if ($LASTEXITCODE -ne 0) { throw 'Launcher compilation failed' }
foreach ($nhlRuntimeName in @('msvcp140.dll', 'msvcp140_atomic_wait.dll', 'vcruntime140.dll', 'vcruntime140_1.dll')) {
    Copy-Item -LiteralPath (Join-Path (Split-Path -Parent $BaseEmulator) $nhlRuntimeName) -Destination $nhlRuntimeOutput
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'defaults') -Destination $nhlBuildOutput -Recurse
$nhlPackageRevision = if ($ExperimentalPro) { 'R9-Pro-TEST' } else { 'R11' }
$nhlPackageArguments = @((Join-Path $PSScriptRoot 'package_full.py'), '--build', $nhlBuildOutput, '--output', (Join-Path $nhlBuildOutput "NHL24-Be-a-Pro-Full-$nhlPackageRevision.zip"))
if ($ExperimentalPro) { $nhlPackageArguments += '--experimental-pro' }
& $Python @nhlPackageArguments
if ($LASTEXITCODE -ne 0) { throw 'Full package verification failed' }
