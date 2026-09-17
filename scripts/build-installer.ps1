[CmdletBinding()]
param(
    [string]$PublishDirectory = "$PSScriptRoot\..\artifacts\VMDesk-SelfContained",
    [string]$OutputPath = "$PSScriptRoot\..\artifacts\VMDesk-Setup-x64.exe"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path
$publish = [System.IO.Path]::GetFullPath($PublishDirectory)
$output = [System.IO.Path]::GetFullPath($OutputPath)
$staging = Join-Path $repoRoot "installer\staging"
$zip = Join-Path $staging "VMDesk-win-x64.zip"
$sed = Join-Path $staging "VMDesk.sed"

if (-not (Test-Path (Join-Path $publish "VMDesk.exe"))) { throw "Publish directory is missing VMDesk.exe: $publish" }
Remove-Item $output -Force -ErrorAction SilentlyContinue
Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $staging | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path $output -Parent) | Out-Null
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip -CompressionLevel Optimal
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
}

if (-not (Test-Path $output)) {
    $ddfPath = Join-Path (Split-Path $output -Parent) ("~" + [System.IO.Path]::GetFileNameWithoutExtension($output) + ".DDF")
    @"
.Set CabinetNameTemplate=$output
.Set CompressionType=LZX
.Set CompressionLevel=7
.Set InfFileName=$(Join-Path (Split-Path $output -Parent) '~VMDesk.inf')
.Set RptFileName=$(Join-Path (Split-Path $output -Parent) '~VMDesk.rpt')
.Set MaxDiskSize=999999488
.Set Cabinet=ON
.Set MaxCabinetSize=999999999
"$zip"
"$(Join-Path $staging 'install.cmd')"
"@ | Set-Content -Path $ddfPath -Encoding ASCII
    if (-not (Test-Path $ddfPath)) {
        throw "Could not create the Cabinet directive file: $ddfPath"
    }

    $makecab = Start-Process -FilePath "$env:WINDIR\System32\makecab.exe" -ArgumentList @('/F', $ddfPath) -NoNewWindow -Wait -PassThru
    if ($makecab.ExitCode -ne 0) {
        throw "makecab failed with exit code $($makecab.ExitCode)."
    }
}

if (-not (Test-Path $output)) {
    throw "The installer output was not created: $output"
}

Get-ChildItem (Split-Path $output -Parent) -Filter "~$([System.IO.Path]::GetFileNameWithoutExtension($output)).*" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
Write-Host "Installer: $output"