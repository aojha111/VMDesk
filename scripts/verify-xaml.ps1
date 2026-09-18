[CmdletBinding()]
param(
    [string]$AppDirectory = ""
)

# Fails the build when the WPF XAML in the app could not be resolved at runtime:
#   1. A Binding assigned to ConverterParameter. ConverterParameter is not a
#      DependencyProperty, so WPF throws XamlParseException while loading the template.
#   2. A StaticResource/DynamicResource name that is never defined. A missing
#      StaticResource throws XamlParseException as soon as the template is used, which on a
#      developer machine with an empty library can stay hidden until a VM exists.
$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path
if ([string]::IsNullOrWhiteSpace($AppDirectory)) {
    $AppDirectory = Join-Path $repoRoot "src\VMDesk.App"
}

$xamlFiles = @(Get-ChildItem -Path $AppDirectory -Recurse -Include *.xaml -File |
    Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' })
if ($xamlFiles.Count -eq 0) {
    throw "No XAML files were found under $AppDirectory."
}

$defined = @{}
foreach ($file in $xamlFiles) {
    foreach ($match in [regex]::Matches((Get-Content -LiteralPath $file.FullName -Raw), 'x:Key="([^"]+)"')) {
        $defined[$match.Groups[1].Value] = $true
    }
}

$failures = @()
foreach ($file in $xamlFiles) {
    $lines = Get-Content -LiteralPath $file.FullName
    for ($index = 0; $index -lt $lines.Count; $index++) {
        $line = $lines[$index]
        $position = $index + 1

        if ($line -match 'ConverterParameter\s*=\s*"\{Binding') {
            $failures += "$($file.Name):$position  ConverterParameter cannot be set to a Binding."
        }

        foreach ($match in [regex]::Matches($line, '\{(StaticResource|DynamicResource)\s+([A-Za-z0-9_.:\-]+)\}')) {
            $kind = $match.Groups[1].Value
            $key = $match.Groups[2].Value
            if (-not $defined.ContainsKey($key)) {
                $failures += "$($file.Name):$position  $kind '$key' is not defined in any XAML dictionary."
            }
        }
    }
}

if ($failures.Count -gt 0) {
    throw ("XAML verification failed:`n  " + ($failures -join "`n  "))
}

Write-Host "XAML verification passed: $($xamlFiles.Count) files, $($defined.Count) resource keys."
