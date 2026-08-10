<#
.SYNOPSIS
  Signs xUnitRevit add-in DLLs with the local self-signed cert from setup-signing.ps1.

.DESCRIPTION
  Uses PowerShell's Set-AuthenticodeSignature (no signtool / Windows SDK required).
  No-ops gracefully if the cert isn't set up yet, so it is safe to call
  unconditionally from a deploy step.

.PARAMETER Path
  One or more files or directories. Directories are searched for *.dll (top level).

.PARAMETER Thumbprint
  Cert thumbprint. Defaults to the value saved in .signing-thumbprint.
#>
param(
  [Parameter(Mandatory = $true)][string[]]$Path,
  [string]$Thumbprint
)

$ErrorActionPreference = "Stop"
$RepoRoot = $PSScriptRoot
$ThumbprintFile = Join-Path $RepoRoot ".signing-thumbprint"

if (-not $Thumbprint) {
  if (Test-Path $ThumbprintFile) {
    $Thumbprint = (Get-Content $ThumbprintFile -Raw).Trim()
  } else {
    Write-Host "sign-addin: no cert configured (run setup-signing.ps1 once). Skipping signing." -ForegroundColor Yellow
    return
  }
}

$cert = Get-Item "Cert:\CurrentUser\My\$Thumbprint" -ErrorAction SilentlyContinue
if (-not $cert) {
  Write-Host "sign-addin: cert $Thumbprint not found in CurrentUser\My. Skipping signing." -ForegroundColor Yellow
  return
}

# Expand inputs to a flat list of .dll files
$files = foreach ($p in $Path) {
  if (Test-Path $p -PathType Container) {
    Get-ChildItem -Path $p -Filter *.dll -File
  } elseif (Test-Path $p) {
    Get-Item $p
  }
}

$signed = 0
foreach ($f in $files) {
  # Skip already-signed-by-us files to save time
  $existing = Get-AuthenticodeSignature $f.FullName
  if ($existing.Status -eq 'Valid' -and $existing.SignerCertificate.Thumbprint -eq $Thumbprint) {
    continue
  }
  $res = Set-AuthenticodeSignature -FilePath $f.FullName -Certificate $cert -HashAlgorithm SHA256
  if ($res.Status -eq 'Valid') {
    $signed++
  } else {
    Write-Warning "Failed to sign $($f.Name): $($res.StatusMessage)"
  }
}

Write-Host "sign-addin: signed $signed file(s) with $($cert.Subject)." -ForegroundColor Green
