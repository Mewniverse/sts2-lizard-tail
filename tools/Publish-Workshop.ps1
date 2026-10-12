<#
.SYNOPSIS
    Builds Lizard Tail and stages (or uploads) it as a Steam Workshop item.

.DESCRIPTION
    Without -Upload this only builds and stages: it leaves a ready workspace
    in %LOCALAPPDATA%\LizardTail-dev\workshop-stage and prints what would be
    sent. Nothing touches Steam.

    With -Upload it runs Mega Crit's own uploader (github.com/megacrit/sts2-mod-uploader),
    which talks to the Steam client that is running and signed in on this PC,
    so the item is published under that Steam account. The uploader starts
    Steam's API as Slay the Spire 2, so do not run it while the same account
    is playing on another PC.

    The Workshop item's ID is kept in workshop\mod_id.txt. The first upload
    creates the item and writes that file; commit it so later uploads update
    the same item instead of making a new one.

.EXAMPLE
    pwsh tools\Publish-Workshop.ps1                  # build and stage only
    pwsh tools\Publish-Workshop.ps1 -Upload          # build, stage and upload
#>
[CmdletBinding()]
param(
    [switch]$Upload,
    [string]$Sts2Path = 'C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2',
    # Overrides workshop.json's visibility: private, unlisted, friends_only or public.
    [string]$Visibility,
    [string]$ChangeNote
)
$ErrorActionPreference = 'Stop'

$repo      = Split-Path $PSScriptRoot -Parent
$workshop  = Join-Path $repo 'workshop'
$devRoot   = Join-Path $env:LOCALAPPDATA 'LizardTail-dev'
$stage     = Join-Path $devRoot 'workshop-stage'
$uploaderSrc = Join-Path $devRoot 'sts2-mod-uploader'
$uploaderBin = Join-Path $devRoot 'mod-uploader'
# The uploader commit this script was checked against.
$uploaderCommit = 'd7b7e6b16c413d5a124f474f9e5104ef01f76ab1'

# 1. Build the mod against the real game, into a folder of its own rather than the game's mods
#    folder, so a Workshop copy and a hand-installed copy never sit side by side.
$proj  = Join-Path $repo 'LizardTail\LizardTail.csproj'
$built = Join-Path $devRoot 'build'
if (Test-Path $built) { Remove-Item $built -Recurse -Force }
dotnet build $proj -c Release "-p:Sts2Path=$Sts2Path" "-p:ModsPath=$built\"
if ($LASTEXITCODE -ne 0) { throw 'mod build failed' }
$dist = Join-Path $built 'LizardTail'
$payload = 'LizardTail.dll', 'LizardTail.json'
foreach ($f in $payload) {
    if (-not (Test-Path (Join-Path $dist $f))) { throw "missing $f in $dist" }
}
$manifest = Get-Content (Join-Path $dist 'LizardTail.json') -Raw | ConvertFrom-Json
$version = $manifest.version
Write-Host "Lizard Tail $version"

# 2. Stage the workspace: content\, image.png, workshop.json, mod_id.txt.
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
$content = Join-Path $stage 'content'
New-Item -ItemType Directory -Path $content -Force | Out-Null
foreach ($f in $payload) { Copy-Item (Join-Path $dist $f) $content }

$image = Join-Path $workshop 'image.png'
if ((Get-Item $image).Length -ge 1MB) { throw 'workshop\image.png must be under 1 MB' }
Copy-Item $image $stage

$cfg = Get-Content (Join-Path $workshop 'workshop.json') -Raw | ConvertFrom-Json
$cfg.description = (Get-Content (Join-Path $workshop 'description.txt') -Raw).Trim()
$cfg.changeNote  = if ($ChangeNote) { $ChangeNote } else { "Version $version" }
if ($Visibility) { $cfg.visibility = $Visibility }
$cfg | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $stage 'workshop.json') -Encoding utf8

$idFile = Join-Path $workshop 'mod_id.txt'
if (Test-Path $idFile) { Copy-Item $idFile $stage }

Write-Host ''
Write-Host "Staged in $stage"
Write-Host "  title:        $($cfg.title)"
Write-Host "  visibility:   $($cfg.visibility)"
Write-Host "  requires:     $($cfg.dependencies -join ', ')"
Write-Host "  content:      $((Get-ChildItem $content).Name -join ', ')"
Write-Host "  item:         $(if (Test-Path $idFile) { Get-Content $idFile } else { 'new item' })"

if (-not $Upload) {
    Write-Host ''
    Write-Host 'Not uploaded. Run again with -Upload to publish.'
    return
}

# 3. Get the uploader (built from source once, then reused).
$exe = Join-Path $uploaderBin 'ModUploader.exe'
if (-not (Test-Path $exe)) {
    if (-not (Test-Path $uploaderSrc)) {
        git clone https://github.com/megacrit/sts2-mod-uploader $uploaderSrc
        if ($LASTEXITCODE -ne 0) { throw 'could not clone the uploader' }
    }
    git -C $uploaderSrc fetch --quiet origin
    git -C $uploaderSrc checkout --quiet $uploaderCommit
    if ($LASTEXITCODE -ne 0) { throw "uploader commit $uploaderCommit not found" }
    # Self-contained so it runs whichever .NET runtime this PC has.
    dotnet publish (Join-Path $uploaderSrc 'ModUploader.csproj') -c Release -r win-x64 --self-contained -o $uploaderBin
    if ($LASTEXITCODE -ne 0) { throw 'uploader build failed' }
}

if (-not (Get-Process steam -ErrorAction SilentlyContinue)) { throw 'Steam is not running on this PC' }
if (Get-Process | Where-Object Name -like 'SlayTheSpire*') { throw 'close the game first' }

# 4. Upload. The uploader writes mod_id.txt into the workspace on a first upload.
Push-Location $uploaderBin
try {
    & $exe upload -w $stage
    $code = $LASTEXITCODE
} finally {
    Pop-Location
}
if ($code -ne 0) { throw "upload failed (exit $code); see $uploaderBin\mod-uploader.log" }

$stagedId = Join-Path $stage 'mod_id.txt'
if (Test-Path $stagedId) {
    Copy-Item $stagedId $idFile -Force
    $id = (Get-Content $idFile -Raw).Trim()
    Write-Host ''
    Write-Host "Published: https://steamcommunity.com/sharedfiles/filedetails/?id=$id"
    Write-Host 'Commit workshop\mod_id.txt so the next upload updates this item.'
}
