[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$OutputRoot = "$PSScriptRoot\..\artifacts\VMDesk-SelfContained"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path
$project = Join-Path $repoRoot "src\VMDesk.App\VMDesk.App.csproj"
$output = [System.IO.Path]::GetFullPath($OutputRoot)
$zip = Join-Path (Split-Path $output -Parent) "VMDesk-Portable-x64.zip"
$artifacts = Join-Path $repoRoot "artifacts"

if (Test-Path $output) { Remove-Item $output -Recurse -Force }
if (Test-Path $zip) { Remove-Item $zip -Force }
Remove-Item (Join-Path $repoRoot 'src\VMDesk.App\obj') -Recurse -Force -ErrorAction SilentlyContinue

dotnet publish $project --configuration $Configuration --runtime win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:DebugType=None --disable-build-servers --output $output
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

Compress-Archive -Path (Join-Path $output '*') -DestinationPath $zip -CompressionLevel Optimal
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
$hashes = @(
    Get-FileHash (Join-Path $artifacts 'VMDesk-Portable-x64.zip') -Algorithm SHA256
    Get-FileHash (Join-Path $artifacts 'VMDesk-SelfContained\VMDesk.exe') -Algorithm SHA256
)
$hashes | ForEach-Object { "$($_.Hash)  $($_.Path.Substring($artifacts.Length + 1))" } |
    Set-Content (Join-Path $artifacts 'SHA256SUMS.txt')
Write-Host "Published: $output"
Write-Host "Portable archive: $zip"

$iscc = Get-Command iscc.exe -ErrorAction SilentlyContinue
if ($null -ne $iscc) {
    $installer = Join-Path $repoRoot "installer\VMDesk.iss"
    if (Test-Path $installer) {
        & $iscc.Source "/DSourceDir=$output" $installer
    }
}