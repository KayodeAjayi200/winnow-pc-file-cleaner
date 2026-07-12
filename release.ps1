# ── Winnow local release helper ───────────────────────────────────────────────
# Usage:  .\release.ps1 -Version 20260712.9
# Bumps InformationalVersion, publishes, builds installer, creates GitHub release
# with notes auto-generated from commits since the last tag.

param(
    [Parameter(Mandatory)][string]$Version
)

$ErrorActionPreference = "Stop"
$iscc = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"

# ── 1. Bump version in csproj ─────────────────────────────────────────────────
Write-Host "Bumping InformationalVersion to $Version..." -ForegroundColor Cyan
$csproj = "FileTinder.csproj"
(Get-Content $csproj) -replace '<InformationalVersion>.*</InformationalVersion>',
    "<InformationalVersion>$Version</InformationalVersion>" |
    Set-Content $csproj

# ── 2. Publish ────────────────────────────────────────────────────────────────
Write-Host "Publishing..." -ForegroundColor Cyan
dotnet publish -c Release -p:PublishProfile=win-x64-singlefile --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Publish failed" }

# ── 3. Build installer ────────────────────────────────────────────────────────
Write-Host "Building installer..." -ForegroundColor Cyan
& $iscc /DAppVersion=$Version installer.iss
if ($LASTEXITCODE -ne 0) { throw "Inno Setup build failed" }

# ── 4. Commit & tag ───────────────────────────────────────────────────────────
Write-Host "Committing..." -ForegroundColor Cyan
git add FileTinder.csproj
git commit -m "Release v$Version" --allow-empty
git tag "v$Version"
git push origin HEAD --tags

# ── 5. Generate changelog from commits since last tag ─────────────────────────
$prevTag = git describe --tags --abbrev=0 HEAD^ 2>$null
if ($prevTag) {
    $allCommits = git log "$prevTag..HEAD" --pretty=format:"%s" |
                  Where-Object { $_ -notmatch "^Release v" -and $_ -notmatch "^Merge " }
} else {
    $allCommits = git log --pretty=format:"%s" | Select-Object -First 30 |
                  Where-Object { $_ -notmatch "^Release v" -and $_ -notmatch "^Merge " }
}

$features = $allCommits | Where-Object {
    $_ -match "\b(add|new|feat|introduc|support|implement|creat|enabl|allow)\b"
}
$bugfixes = $allCommits | Where-Object {
    $_ -match "\b(fix|bug|crash|error|broken|revert|patch|correct|resolv)\b"
}
$other    = $allCommits | Where-Object {
    $_ -notin $features -and $_ -notin $bugfixes
}

$sections = @()
if ($features) {
    $sections += "### ✨ What's New"
    $sections += $features | ForEach-Object { "- $_" }
    $sections += ""
}
if ($bugfixes) {
    $sections += "### 🐛 Bug Fixes"
    $sections += $bugfixes | ForEach-Object { "- $_" }
    $sections += ""
}
if ($other) {
    $sections += "### 🔧 Improvements"
    $sections += $other | ForEach-Object { "- $_" }
    $sections += ""
}
if (-not $sections) {
    $sections = @("No notable changes.")
}

$notes = ($sections -join "`n") + @"

---
### Install
Download and run the installer below. The app will notify you of future updates automatically.

### Upgrade
Already have Winnow? Click **Download & Install** inside the app, or run the installer — it upgrades in-place.
"@

# ── 6. Create GitHub release ──────────────────────────────────────────────────
Write-Host "Creating GitHub release v$Version..." -ForegroundColor Cyan
$installer = "installer\WinnowSetup-$Version.exe"
gh release create "v$Version" $installer `
    --title "Winnow v$Version" `
    --notes $notes

Write-Host "Done! v$Version released." -ForegroundColor Green
