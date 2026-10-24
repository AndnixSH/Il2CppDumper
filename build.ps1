# Build Il2CppDumper GUI and CLI for 32-bit and 64-bit Windows.
# Each architecture lands in <OutputDir>\Il2CppDumper-<version>-<runtime>: both
# single-file exes, sharing config.json and the IDA/Ghidra/Binary Ninja scripts.
# The x86 builds can load 32-bit GameAssembly.dll files with the custom PE loader.
#
#   .\build.ps1                  framework-dependent (needs the .NET 10 runtime)
#   .\build.ps1 -SelfContained   bundles the .NET runtime, no install needed
#
# Run from anywhere; paths are resolved relative to this script.
param(
  [string]$Configuration = 'Release',
  [string]$OutputDir = (Join-Path $PSScriptRoot 'publish'),
  [switch]$SelfContained
)
$ErrorActionPreference = 'Stop'

$version = ([xml](Get-Content (Join-Path $PSScriptRoot 'Directory.Build.props'))).Project.PropertyGroup.Version
$projects = [ordered]@{
  GUI = Join-Path $PSScriptRoot 'Il2CppDumper.GUI\Il2CppDumper.GUI.csproj'
  CLI = Join-Path $PSScriptRoot 'Il2CppDumper.CLI\Il2CppDumper.CLI.csproj'
}
$runtimes = 'win-x86', 'win-x64'

$publishArgs = @('-c', $Configuration, '-p:PublishSingleFile=true', '--nologo')
if ($SelfContained) {
  # Also pack WPF's native DLLs into the exe instead of placing them next to it
  $publishArgs += '--self-contained', '-p:IncludeNativeLibrariesForSelfExtract=true'
} else {
  $publishArgs += '--no-self-contained'
}

$outputs = @()
foreach ($runtime in $runtimes) {
  $target = Join-Path $OutputDir "Il2CppDumper-$version-$runtime"
  # Start from an empty folder so files from an earlier build don't linger
  if (Test-Path $target) { Remove-Item $target -Recurse -Force }
  foreach ($name in $projects.Keys) {
    Write-Host "== building $name for $runtime ==" -ForegroundColor Cyan
    dotnet publish $projects[$name] -r $runtime -o $target @publishArgs
    if ($LASTEXITCODE) { throw "$name build for $runtime failed ($LASTEXITCODE)" }
  }
  $outputs += $target
}

$outputs | ForEach-Object {
  Get-ChildItem $_ -Filter '*.exe' | ForEach-Object {
    [pscustomobject]@{ Output = $_.DirectoryName; Exe = $_.Name; 'Size (MB)' = [math]::Round($_.Length / 1MB, 1) }
  }
} | Format-Table -AutoSize
Write-Host 'OK'
