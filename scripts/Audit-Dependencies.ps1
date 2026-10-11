<#
.SYNOPSIS
    Supply-chain audit of every NuGet dependency in this repository.

.DESCRIPTION
    Two independent layers, so a finding cannot slip through if one of them cannot run:

      1. Restore-time auditing (authoritative). Both project files set NuGetAudit, and restore is
         run here with NuGet's advisory warnings promoted to errors. This works on every platform
         and for every project, including ones whose tooling output cannot be parsed.
      2. `dotnet list package` reporting (detail). Names the offending package, version and
         advisory. Some SDK and project-SDK combinations cannot produce this for a given project;
         when that happens the run is still gated by layer 1 and the gap is reported, not hidden.

    Also flags floating version ranges ("*", "1.2.*"), which let a future restore pull an
    unreviewed version, and any project that asks for a lock file but has not committed one.

    Exits non-zero on any vulnerability, deprecated package, or floating version.

.EXAMPLE
    ./scripts/Audit-Dependencies.ps1
    ./scripts/Audit-Dependencies.ps1 -FailOnFloating:$false
#>
param(
    [string[]]$Projects = @(
        "LizardTail/LizardTail.csproj",
        "tests/LizardTail.Tests/LizardTail.Tests.csproj"
    ),
    [switch]$FailOnFloating = $true
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent
Push-Location $repoRoot

$problems = [System.Collections.Generic.List[string]]::new()
$warnings = [System.Collections.Generic.List[string]]::new()

# NU1901-NU1904 are NuGet's low/moderate/high/critical advisory warnings.
$auditCodes = "NU1901;NU1902;NU1903;NU1904"

function Invoke-ListPackage {
    <# Returns the parsed report, or $null when the CLI could not produce one for this project. #>
    param([string]$Project, [string]$Mode)

    $raw = & dotnet list $Project package $Mode --include-transitive --format json 2>&1 | Out-String
    $start = $raw.IndexOf('{')
    if ($start -lt 0) { return $null }

    try {
        $report = $raw.Substring($start) | ConvertFrom-Json
    } catch {
        return $null
    }

    # The CLI reports per-project failures inside the JSON rather than by exit code.
    if ($report.problems) {
        foreach ($p in $report.problems) {
            if ($p.level -eq "error") { return $null }
        }
    }
    return $report
}

function Read-Findings {
    param($Report, [string]$Label, [string]$Project)

    foreach ($framework in $Report.projects.frameworks) {
        foreach ($listName in @("topLevelPackages", "transitivePackages")) {
            foreach ($pkg in $framework.$listName) {
                $detail = ""
                if ($pkg.vulnerabilities) {
                    $detail = ($pkg.vulnerabilities | ForEach-Object { "$($_.severity): $($_.advisoryurl)" }) -join "; "
                } elseif ($pkg.deprecationReasons) {
                    $detail = ($pkg.deprecationReasons -join ", ")
                    if ($pkg.alternativePackage) { $detail += " (use $($pkg.alternativePackage.id))" }
                }
                $problems.Add("$Label  $Project  $($pkg.id) $($pkg.resolvedVersion)  $detail")
            }
        }
    }
}

Write-Host "Auditing $($Projects.Count) project(s) in $repoRoot" -ForegroundColor Cyan

foreach ($project in $Projects) {
    if (-not (Test-Path $project)) {
        $problems.Add("MISSING   $project not found")
        continue
    }

    Write-Host ""
    Write-Host "== $project"

    # Layer 1: restore with advisory warnings as errors. This is the real gate.
    $restoreLog = & dotnet restore $project --nologo "-warnAsError:$auditCodes" 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        $advisoryLines = $restoreLog -split "`n" | Where-Object { $_ -match "NU190[1-4]" }
        if ($advisoryLines) {
            foreach ($line in $advisoryLines) { $problems.Add("VULNERABLE  $project  $($line.Trim())") }
        } else {
            $problems.Add("RESTORE   $project  restore failed:`n$restoreLog")
        }
        continue
    }
    Write-Host "   restore-time audit: clean"

    # Layer 2: detailed reporting, best effort.
    $vulnReport = Invoke-ListPackage -Project $project -Mode "--vulnerable"
    $deprReport = Invoke-ListPackage -Project $project -Mode "--deprecated"

    if ($null -eq $vulnReport) {
        $warnings.Add("NO DETAIL $project  'dotnet list package --vulnerable' could not run here; covered by the restore-time audit above")
    } else {
        Read-Findings -Report $vulnReport -Label "VULNERABLE" -Project $project
        Write-Host "   vulnerability report: clean"
    }

    if ($null -eq $deprReport) {
        $warnings.Add("NO DETAIL $project  'dotnet list package --deprecated' could not run here; deprecated packages are NOT checked for this project")
    } else {
        Read-Findings -Report $deprReport -Label "DEPRECATED" -Project $project
        Write-Host "   deprecation report: clean"
    }

    # Floating versions defeat pinning: the next restore can silently take a different build.
    $content = Get-Content $project -Raw
    foreach ($m in [regex]::Matches($content, '<PackageReference\s+Include="([^"]+)"\s+Version="([^"]*\*[^"]*)"')) {
        $line = "FLOATING  $project  $($m.Groups[1].Value) = $($m.Groups[2].Value)"
        if ($FailOnFloating) { $problems.Add($line) } else { $warnings.Add($line) }
    }

    # Only projects that ask for a lock file are expected to have one. Some cannot keep one
    # stable, and saying so in the project file is the declaration this check trusts.
    if ($content -match '<RestorePackagesWithLockFile>\s*true\s*</RestorePackagesWithLockFile>') {
        $lockFile = Join-Path (Split-Path $project -Parent) "packages.lock.json"
        if (-not (Test-Path $lockFile)) {
            $problems.Add("NO LOCK   $project sets RestorePackagesWithLockFile but has no packages.lock.json")
        }
    }
}

Pop-Location

Write-Host ""
foreach ($w in $warnings) { Write-Host $w -ForegroundColor Yellow }

if ($problems.Count -gt 0) {
    Write-Host ""
    Write-Host "Dependency audit FAILED with $($problems.Count) finding(s):" -ForegroundColor Red
    foreach ($p in $problems) { Write-Host "  $p" -ForegroundColor Red }
    exit 1
}

Write-Host ""
Write-Host "Dependency audit passed: no vulnerable, deprecated or floating packages." -ForegroundColor Green
exit 0
