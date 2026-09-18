[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path
$project = Join-Path $repoRoot "src\VMDesk.App\VMDesk.App.csproj"
$artifacts = Join-Path $repoRoot "artifacts"
$stage = Join-Path $artifacts ".stage"
$exe = Join-Path $artifacts "VMDesk.exe"

New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
Remove-Item $exe -Force -ErrorAction SilentlyContinue

# Fail fast on XAML that would only crash once a VM exists in the library.
& (Join-Path $PSScriptRoot "verify-xaml.ps1")

dotnet publish $project --configuration $Configuration --runtime win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None --disable-build-servers --output $stage
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

Copy-Item (Join-Path $stage "VMDesk.exe") $exe -Force
Remove-Item $stage -Recurse -Force
Write-Host "Standalone executable: $exe"

if (-not $SkipInstaller) {
    & (Join-Path $PSScriptRoot "build-installer.ps1") -PublishExe $exe
}

$hashLines = @()
if (Test-Path $exe) {
    $hashLines += Get-FileHash $exe -Algorithm SHA256
}
$setup = Join-Path $artifacts "VMDesk-Setup-x64.exe"
if (Test-Path $setup) {
    $hashLines += Get-FileHash $setup -Algorithm SHA256
}
$hashLines | ForEach-Object { "$($_.Hash)  $($_.Path.Substring($artifacts.Length + 1))" } |
    Set-Content (Join-Path $artifacts "SHA256SUMS.txt")
