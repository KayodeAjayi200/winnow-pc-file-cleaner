<#
.SYNOPSIS
  Build a Microsoft Store-ready MSIX package for Winnow.

.DESCRIPTION
  1. Updates Package.appxmanifest with the supplied version.
  2. Publishes in Store configuration (self-contained, MSIX output).
  3. Copies the resulting .msix to the repo root for easy upload.

.PARAMETER Version
  4-part version number for the Store (e.g. "1.2.1.0").
  Must match the format required by MSIX: Major.Minor.Build.Revision.

.EXAMPLE
  .\build-store.ps1 -Version 1.2.1.0

.NOTES
  BEFORE your first Store submission:
  ─────────────────────────────────────────────────────────────────────────
  1. Register at https://partner.microsoft.com/dashboard ($19 one-time fee)
  2. Create a new App → Reserve a name → Go to "Product identity"
  3. Copy the Package/Identity Name  → update Package.appxmanifest Identity/@Name
  4. Copy the Package/Identity Publisher (CN=...) → update Identity/@Publisher
  5. Update Properties/PublisherDisplayName to your real name/company
  ─────────────────────────────────────────────────────────────────────────
  After setting those values, run this script and upload the .msix to
  Partner Center → Packages.
#>
param(
    [string]$Version = "1.2.0.0"
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

# ── 1. Validate version format ──────────────────────────────────────────
if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    Write-Error "Version must be in Major.Minor.Build.Revision format (e.g. 1.2.0.0)"
    exit 1
}

# ── 2. Patch version in Package.appxmanifest ────────────────────────────
$manifest = "Package.appxmanifest"
$xml = [xml](Get-Content $manifest -Raw)
$xml.Package.Identity.Version = $Version
$xml.Save((Resolve-Path $manifest))
Write-Host "✅ Manifest version set to $Version"

# ── 3. Publish (Store configuration, MSIX output) ───────────────────────
Write-Host "📦 Publishing Store build..."
dotnet publish -c Store -r win-x64 `
    -p:AppxPackageSigningEnabled=false `
    -p:GenerateAppxPackageOnBuild=true `
    --self-contained
if ($LASTEXITCODE -ne 0) { Write-Error "dotnet publish failed"; exit 1 }

# ── 4. Locate the generated .msix ───────────────────────────────────────
$msix = Get-ChildItem -Recurse -Filter "*.msix" | `
        Where-Object { $_.DirectoryName -like "*AppPackages*" } | `
        Sort-Object LastWriteTime -Descending | Select-Object -First 1

if (-not $msix) {
    # Fallback: look anywhere under bin/
    $msix = Get-ChildItem "bin" -Recurse -Filter "*.msix" | `
            Sort-Object LastWriteTime -Descending | Select-Object -First 1
}

if ($msix) {
    $dest = "Winnow-$Version-Store.msix"
    Copy-Item $msix.FullName $dest -Force
    Write-Host ""
    Write-Host "✅ Store package ready: $dest"
    Write-Host ""
    Write-Host "Next steps:"
    Write-Host "  1. Log in to https://partner.microsoft.com/dashboard"
    Write-Host "  2. Open your Winnow app → Submissions → New submission"
    Write-Host "  3. Upload $dest in the Packages step"
} else {
    Write-Host "⚠️  Build succeeded but .msix not found in output. Check bin\ manually."
}
