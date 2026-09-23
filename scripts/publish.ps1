[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path
$project = Join-Path $repoRoot "src\VMDesk.App\VMDesk.App.csproj"
$dist = Join-Path $repoRoot "dist"
$stage = Join-Path $dist ".stage"
$exe = Join-Path $dist "VMDesk.exe"

New-Item -ItemType Directory -Force -Path $dist | Out-Null
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
Remove-Item $exe -Force -ErrorAction SilentlyContinue

# Fail fast on XAML that would only crash once a VM exists in the library.
& (Join-Path $PSScriptRoot "verify-xaml.ps1")

# Keep the csproj's embedded debug info: the crash dialog and the log file are the only
# evidence a user-reported failure leaves behind, and DebugType=None erases the line numbers.
dotnet publish $project --configuration $Configuration --runtime win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    --disable-build-servers --output $stage
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

$staged = Join-Path $stage "VMDesk.exe"
if (-not (Test-Path $staged)) { throw "publish produced no VMDesk.exe in $stage." }

# A framework-dependent build runs fine here and only fails on the user's machine with
# "You must install .NET Desktop Runtime". Assert the runtime is really inside the bundle.
$bundle = [System.Text.Encoding]::ASCII.GetString([System.IO.File]::ReadAllBytes($staged))
$required = @(
    "coreclr",                    # the runtime itself, not a shared-framework lookup
    "hostpolicy",
    "System.Private.CoreLib.dll",
    "Microsoft.WindowsDesktop.App",
    "PresentationFramework.dll",  # WPF
    "e_sqlite3.dll"               # SQLite native, extracted at first run
)
$missing = @($required | Where-Object { -not $bundle.Contains($_) })
if ($missing.Count -gt 0) {
    throw "VMDesk.exe is not self-contained; missing from bundle: $($missing -join ', '). A machine without the .NET desktop runtime would fail to start."
}
if ((Get-Item $staged).Length -lt 40MB) {
    throw "VMDesk.exe is only $([math]::Round((Get-Item $staged).Length / 1MB, 1)) MB; a bundled desktop runtime is ~70 MB. Refusing to ship a framework-dependent exe."
}
Write-Host "Verified self-contained bundle: desktop runtime, WPF and SQLite natives are embedded (no .NET install needed on the target)."

Copy-Item $staged $exe -Force
Remove-Item $stage -Recurse -Force
Write-Host "Standalone executable: $exe"

if (-not $SkipInstaller) {
    & (Join-Path $PSScriptRoot "build-installer.ps1") -PublishExe $exe
}

$hashLines = @()
if (Test-Path $exe) {
    $hashLines += Get-FileHash $exe -Algorithm SHA256
}
$setup = Join-Path $dist "VMDesk-Setup-x64.exe"
if (Test-Path $setup) {
    $hashLines += Get-FileHash $setup -Algorithm SHA256
}
$hashLines | ForEach-Object { "$($_.Hash)  $($_.Path.Substring($dist.Length + 1))" } |
    Set-Content (Join-Path $dist "SHA256SUMS.txt")
