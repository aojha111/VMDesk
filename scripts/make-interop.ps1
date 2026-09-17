# Generates COM interop assemblies for the Microsoft RDP ActiveX control (mstscax.dll).
# Outputs: src/libs/MSTSCLib.dll and src/libs/AxMSTSCLib.dll (committed to the repo so the
# build does not depend on the Windows SDK interop tools).
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $repo 'src\libs'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$tools = 'C:\Program Files (x86)\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.8 Tools\x64'
$tlbimp = Join-Path $tools 'TlbImp.exe'
$aximp  = Join-Path $tools 'AxImp.exe'
if (-not (Test-Path $tlbimp)) { $tlbimp = 'C:\Program Files (x86)\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.8 Tools\TlbImp.exe' }
if (-not (Test-Path $aximp))  { $aximp  = 'C:\Program Files (x86)\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.8 Tools\AxImp.exe' }
if (-not (Test-Path $tlbimp)) { throw "TlbImp.exe not found. Install the Windows SDK / .NET Framework 4.8 developer tools." }
if (-not (Test-Path $aximp))  { throw "AxImp.exe not found. Install the Windows SDK / .NET Framework 4.8 developer tools." }

$mstscax = Join-Path $env:SystemRoot 'System32\mstscax.dll'
if (-not (Test-Path $mstscax)) { throw "mstscax.dll not found at $mstscax" }

$tlbOut = Join-Path $outDir 'MSTSCLib.dll'
$axOut  = Join-Path $outDir 'AxMSTSCLib.dll'

& $tlbimp $mstscax /namespace:MSTSCLib /out:$tlbOut /silent
if ($LASTEXITCODE -ne 0) { throw "TlbImp failed with exit code $LASTEXITCODE" }

& $aximp $mstscax /out:$axOut /rcw:$tlbOut /silent
if ($LASTEXITCODE -ne 0) { throw "AxImp failed with exit code $LASTEXITCODE" }

Write-Host "Interop assemblies written:"
Write-Host "  $tlbOut"
Write-Host "  $axOut"
