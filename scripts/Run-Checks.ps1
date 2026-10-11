<#
.SYNOPSIS
    Runs every check this repository has: unit tests, dependency audit, and a mod build.

.DESCRIPTION
    The same checks CI runs, plus the mod compile that CI cannot do because it has no copy of the
    game. Use this before committing.

.EXAMPLE
    ./scripts/Run-Checks.ps1
    ./scripts/Run-Checks.ps1 -SkipBuild   # when the game is not installed on this machine
#>
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent
Push-Location $repoRoot

$failures = [System.Collections.Generic.List[string]]::new()

function Invoke-Step {
    param([string]$Name, [scriptblock]$Action)

    Write-Host ""
    Write-Host "=== $Name" -ForegroundColor Cyan
    & $Action
    if ($LASTEXITCODE -ne 0) {
        $failures.Add($Name)
        Write-Host "$Name FAILED" -ForegroundColor Red
    }
}

Invoke-Step "Unit tests" { dotnet test tests/LizardTail.Tests/LizardTail.Tests.csproj --nologo --verbosity minimal }

Invoke-Step "Dependency audit" { & "$PSScriptRoot/Audit-Dependencies.ps1" }

if (-not $SkipBuild) {
    Invoke-Step "Mod build" { dotnet build LizardTail/LizardTail.csproj -c Release --nologo --verbosity minimal }
}

Pop-Location

Write-Host ""
if ($failures.Count -gt 0) {
    Write-Host "FAILED: $($failures -join ', ')" -ForegroundColor Red
    exit 1
}

Write-Host "All checks passed." -ForegroundColor Green
exit 0
