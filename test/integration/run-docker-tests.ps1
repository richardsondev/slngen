<#
.SYNOPSIS
    Runs SlnGen Docker-based SDK compatibility tests.

.DESCRIPTION
    Builds and runs Docker containers that validate SlnGen builds and tests
    pass against specific .NET SDK versions. This catches compatibility issues
    early, such as those reported with .NET SDK 9.0.201/202 and 10.0.103.

    SDK versions are pinned by tag@sha256 digest in each Dockerfile so that
    Dependabot can automatically propose updates.

.PARAMETER SdkVersions
    Which SDK major versions to test. Defaults to all stable: 8, 9, 10.
    Use "11" to include the .NET 11 preview (may fail — it's informational).

.EXAMPLE
    # Run all stable SDK tests
    .\run-docker-tests.ps1

.EXAMPLE
    # Test only .NET 9 SDK
    .\run-docker-tests.ps1 -SdkVersions 9

.EXAMPLE
    # Include .NET 11 preview
    .\run-docker-tests.ps1 -SdkVersions 8,9,10,11
#>

[CmdletBinding()]
param(
    [ValidateSet("8", "9", "10", "11")]
    [string[]]$SdkVersions = @("8", "9", "10")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$dockerDir = $PSScriptRoot

# Map major versions to Dockerfile names
$sdkConfigs = @{
    "8"  = @{ Dockerfile = "Dockerfile.sdk8"          }
    "9"  = @{ Dockerfile = "Dockerfile.sdk9"          }
    "10" = @{ Dockerfile = "Dockerfile.sdk10"         }
    "11" = @{ Dockerfile = "Dockerfile.sdk11-preview" }
}

$results = @()
$overallSuccess = $true

foreach ($ver in $SdkVersions) {
    $config = $sdkConfigs[$ver]
    $dockerfile = Join-Path $dockerDir $config.Dockerfile
    $imageName = "slngen-test-sdk$ver"

    Write-Host ""
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host " Testing .NET SDK $ver (Dockerfile: $($config.Dockerfile))" -ForegroundColor Cyan
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host ""

    $buildArgs = @(
        "build"
        "-f", $dockerfile
        "-t", $imageName
        $repoRoot
    )

    Write-Host "docker $($buildArgs -join ' ')" -ForegroundColor DarkGray
    & docker @buildArgs
    $exitCode = $LASTEXITCODE

    $status = if ($exitCode -eq 0) { "PASSED" } else { "FAILED" }
    $color  = if ($exitCode -eq 0) { "Green"  } else { "Red"    }

    if ($exitCode -ne 0) {
        $overallSuccess = $false
    }

    Write-Host ""
    Write-Host " .NET SDK ${ver}: $status" -ForegroundColor $color
    Write-Host ""

    $results += [PSCustomObject]@{
        SDK      = "SDK $ver"
        Status   = $status
        ExitCode = $exitCode
    }
}

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host " Summary" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
$results | Format-Table -AutoSize

if (-not $overallSuccess) {
    Write-Host "One or more SDK tests FAILED." -ForegroundColor Red
    exit 1
} else {
    Write-Host "All SDK tests PASSED." -ForegroundColor Green
    exit 0
}
