<#
.SYNOPSIS
    Validates .resx resource files for completeness and correctness.

.DESCRIPTION
    This script validates resource files by checking:
    - All culture-specific files have the same keys as the default file
    - No duplicate keys within a file
    - No empty or whitespace-only values
    - File references point to existing files
    - Valid XML structure

.PARAMETER ResourcesPath
    Path to the directory containing .resx files. Defaults to current directory.

.PARAMETER BaseFileName
    Base name of the resource file (without culture suffix). Default: "Resources"

.PARAMETER Cultures
    Comma-separated list of cultures to validate. If not specified, validates all found cultures.

.PARAMETER FailOnMissing
    If specified, script exits with error code if translations are missing.

.EXAMPLE
    .\validate-resources.ps1 -ResourcesPath ".\Resources" -BaseFileName "Resources"

.EXAMPLE
    .\validate-resources.ps1 -Cultures "es,fr,de" -FailOnMissing
#>

param(
    [string]$ResourcesPath = ".",
    [string]$BaseFileName = "Resources",
    [string]$Cultures = "",
    [switch]$FailOnMissing
)

$ErrorActionPreference = "Stop"

# Helper function to load .resx file
function Get-ResxKeys {
    param([string]$FilePath)

    $keys = @{}

    if (-not (Test-Path $FilePath)) {
        Write-Warning "File not found: $FilePath"
        return $keys
    }

    try {
        [xml]$resx = Get-Content $FilePath
        foreach ($data in $resx.root.data) {
            $keyName = $data.name
            $value = $data.value

            if ($keys.ContainsKey($keyName)) {
                Write-Warning "Duplicate key '$keyName' in $FilePath"
            }

            $keys[$keyName] = $value
        }
    }
    catch {
        Write-Error "Failed to parse $FilePath : $_"
    }

    return $keys
}

# Helper function to check file references
function Test-FileReferences {
    param(
        [string]$FilePath,
        [hashtable]$Keys
    )

    $baseDir = Split-Path -Parent $FilePath
    $issues = @()

    foreach ($key in $Keys.Keys) {
        $value = $Keys[$key]
        if ($value -match ';System\.Resources\.ResXFileRef') {
            $filePath = ($value -split ';')[0]
            $fullPath = Join-Path $baseDir $filePath

            if (-not (Test-Path $fullPath)) {
                $issues += "Missing file reference: $filePath (key: $key)"
            }
        }
    }

    return $issues
}

Write-Host "====================================" -ForegroundColor Cyan
Write-Host "Resource File Validation" -ForegroundColor Cyan
Write-Host "====================================" -ForegroundColor Cyan
Write-Host ""

# Find base resource file
$baseFile = Join-Path $ResourcesPath "$BaseFileName.resx"
if (-not (Test-Path $baseFile)) {
    Write-Error "Base resource file not found: $baseFile"
    exit 1
}

Write-Host "Base file: $baseFile" -ForegroundColor Green

# Load base keys
$baseKeys = Get-ResxKeys -FilePath $baseFile
Write-Host "Base file contains $($baseKeys.Count) keys" -ForegroundColor Gray
Write-Host ""

# Determine cultures to validate
$cultureFiles = @()

if ($Cultures) {
    $cultureList = $Cultures -split ','
    foreach ($culture in $cultureList) {
        $culture = $culture.Trim()
        $cultureFile = Join-Path $ResourcesPath "$BaseFileName.$culture.resx"
        if (Test-Path $cultureFile) {
            $cultureFiles += @{
                Culture = $culture
                Path    = $cultureFile
            }
        }
        else {
            Write-Warning "Culture file not found: $cultureFile"
        }
    }
}
else {
    # Auto-detect all culture files
    $pattern = "$BaseFileName.*.resx"
    Get-ChildItem -Path $ResourcesPath -Filter $pattern | ForEach-Object {
        $fileName = $_.Name
        if ($fileName -match "$BaseFileName\.(.+)\.resx$") {
            $culture = $Matches[1]
            $cultureFiles += @{
                Culture = $culture
                Path    = $_.FullName
            }
        }
    }
}

if ($cultureFiles.Count -eq 0) {
    Write-Warning "No culture-specific resource files found"
    Write-Host ""
    Write-Host "Validation complete." -ForegroundColor Green
    exit 0
}

Write-Host "Found $($cultureFiles.Count) culture file(s) to validate:" -ForegroundColor Gray
foreach ($cf in $cultureFiles) {
    Write-Host "  - $($cf.Culture)" -ForegroundColor Gray
}
Write-Host ""

# Validate each culture file
$totalErrors = 0
$totalWarnings = 0
$validationResults = @()

foreach ($cultureFile in $cultureFiles) {
    $culture = $cultureFile.Culture
    $path = $cultureFile.Path

    Write-Host "Validating: $culture" -ForegroundColor Cyan

    $cultureKeys = Get-ResxKeys -FilePath $path
    Write-Host "  Keys: $($cultureKeys.Count)" -ForegroundColor Gray

    $errors = @()
    $warnings = @()

    # Check for missing keys
    $missingKeys = $baseKeys.Keys | Where-Object { -not $cultureKeys.ContainsKey($_) }
    if ($missingKeys.Count -gt 0) {
        $warnings += "Missing $($missingKeys.Count) translation(s):"
        foreach ($key in $missingKeys) {
            $warnings += "  - $key"
        }
    }

    # Check for extra keys (not in base)
    $extraKeys = $cultureKeys.Keys | Where-Object { -not $baseKeys.ContainsKey($_) }
    if ($extraKeys.Count -gt 0) {
        $warnings += "Extra $($extraKeys.Count) key(s) not in base:"
        foreach ($key in $extraKeys) {
            $warnings += "  - $key"
        }
    }

    # Check for empty values
    foreach ($key in $cultureKeys.Keys) {
        $value = $cultureKeys[$key]
        if ([string]::IsNullOrWhiteSpace($value)) {
            $warnings += "Empty value for key: $key"
        }
    }

    # Check file references
    $fileRefIssues = Test-FileReferences -FilePath $path -Keys $cultureKeys
    if ($fileRefIssues.Count -gt 0) {
        $errors += $fileRefIssues
    }

    # Report results
    if ($errors.Count -gt 0) {
        Write-Host "  ERRORS: $($errors.Count)" -ForegroundColor Red
        foreach ($error in $errors) {
            Write-Host "    $error" -ForegroundColor Red
        }
        $totalErrors += $errors.Count
    }

    if ($warnings.Count -gt 0) {
        Write-Host "  WARNINGS: $($warnings.Count)" -ForegroundColor Yellow
        foreach ($warning in $warnings) {
            Write-Host "    $warning" -ForegroundColor Yellow
        }
        $totalWarnings += $warnings.Count
    }

    if ($errors.Count -eq 0 -and $warnings.Count -eq 0) {
        Write-Host "  ✓ OK" -ForegroundColor Green
    }

    Write-Host ""

    $validationResults += @{
        Culture  = $culture
        Errors   = $errors.Count
        Warnings = $warnings.Count
    }
}

# Summary
Write-Host "====================================" -ForegroundColor Cyan
Write-Host "Validation Summary" -ForegroundColor Cyan
Write-Host "====================================" -ForegroundColor Cyan
Write-Host "Total Errors: $totalErrors" -ForegroundColor $(if ($totalErrors -gt 0) { "Red" } else { "Green" })
Write-Host "Total Warnings: $totalWarnings" -ForegroundColor $(if ($totalWarnings -gt 0) { "Yellow" } else { "Green" })
Write-Host ""

# Exit code
if ($totalErrors -gt 0) {
    Write-Host "Validation FAILED" -ForegroundColor Red
    exit 1
}
elseif ($FailOnMissing -and $totalWarnings -gt 0) {
    Write-Host "Validation FAILED (warnings treated as errors)" -ForegroundColor Red
    exit 1
}
else {
    Write-Host "Validation PASSED" -ForegroundColor Green
    exit 0
}
