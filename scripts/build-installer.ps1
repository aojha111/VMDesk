[CmdletBinding()]
param(
    # Path to the single-file VMDesk.exe produced by publish.ps1.
    [string]$PublishExe = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path
$artifacts = Join-Path $repoRoot "artifacts"
$output = Join-Path $artifacts "VMDesk-Setup-x64.exe"
$staging = Join-Path $repoRoot "installer\staging"
$zip = Join-Path $staging "VMDesk-win-x64.zip"
$sed = Join-Path $staging "VMDesk.sed"

if ([string]::IsNullOrWhiteSpace($PublishExe)) {
    $PublishExe = Join-Path $artifacts "VMDesk.exe"
}
if (-not (Test-Path $PublishExe)) { throw "Standalone VMDesk.exe not found: $PublishExe" }

Remove-Item $output -Force -ErrorAction SilentlyContinue
Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $staging | Out-Null
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null

# The installer extracts VMDesk.exe to %LOCALAPPDATA%\VMDesk and creates shortcuts.
Compress-Archive -Path $PublishExe -DestinationPath $zip -CompressionLevel Optimal
Copy-Item (Join-Path $repoRoot "installer\install.cmd") (Join-Path $staging "install.cmd")

$sedContent = @"
[Version]
Class=IEXPRESS
SEDVersion=3
[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=1
HideExtractAnimation=1
UseLongFileName=1
InsideCompressed=1
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=I
InstallPrompt=VMDesk will install for the current Windows user.
DisplayLicense=
FinishMessage=VMDesk installation completed.
TargetName=$output
FriendlyName=VMDesk Setup
AppLaunched=install.cmd
PostInstallCmd=<None>
AdminQuietInstCmd=
UserQuietInstCmd=
SourceFiles=SourceFiles
[SourceFiles]
SourceFiles0=$staging
[SourceFiles0]
VMDesk-win-x64.zip=
install.cmd=
"@
Set-Content -Path $sed -Value $sedContent -Encoding ASCII

$iexpress = Get-Command "$env:WINDIR\System32\iexpress.exe" -ErrorAction SilentlyContinue
if ($null -ne $iexpress) {
    & $iexpress.Source /N /Q $sed
    # IExpress returns before the package is fully written; wait for completion.
    $deadline = (Get-Date).AddMinutes(5)
    while (-not (Test-Path $output) -and (Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 2
    }
}

if (-not (Test-Path $output)) {
    throw "The installer output was not created: $output"
}

Get-ChildItem $artifacts -Filter "~VMDesk*" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
Write-Host "Installer: $output"
