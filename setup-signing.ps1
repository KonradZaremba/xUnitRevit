<#
.SYNOPSIS
  One-time setup of a LOCAL self-signed code-signing certificate for xUnitRevit.

.DESCRIPTION
  Revit shows a "Security - Unsigned Add-In" prompt when it loads an add-in whose
  DLL is not signed by a trusted publisher. The add-in cannot suppress its OWN load
  prompt (the dialog fires before the add-in's OnStartup registers its suppressor),
  so the DLLs must be signed with a cert Revit already trusts.

  This creates a self-signed code-signing cert and installs it into the CURRENT USER
  stores (no admin required):
    - Trusted Root CA      -> so the Authenticode signature validates
    - Trusted Publisher    -> so Revit auto-loads the add-in without prompting

  The thumbprint is written to .signing-thumbprint so sign-addin.ps1 /
  run-revit-tests.ps1 can find the cert. Run this ONCE per machine/user.

  This is for LOCAL development/CI only. It does not help on other people's machines
  unless they trust the same cert. For distribution you need a real code-signing cert.
#>
param(
  [string]$CertName = "xUnitRevit Local Dev",
  [int]$ValidYears = 5
)

$ErrorActionPreference = "Stop"
$RepoRoot = $PSScriptRoot
$ThumbprintFile = Join-Path $RepoRoot ".signing-thumbprint"

# Reuse an existing cert with this subject if present
$subject = "CN=$CertName"
$existing = Get-ChildItem Cert:\CurrentUser\My |
  Where-Object { $_.Subject -eq $subject -and $_.NotAfter -gt (Get-Date) } |
  Sort-Object NotAfter -Descending | Select-Object -First 1

if ($existing) {
  Write-Host "Reusing existing certificate ($($existing.Thumbprint))." -ForegroundColor Cyan
  $cert = $existing
} else {
  Write-Host "Creating self-signed code-signing certificate '$subject'..." -ForegroundColor Cyan
  $cert = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject $subject `
    -CertStoreLocation Cert:\CurrentUser\My `
    -KeyUsage DigitalSignature `
    -KeyExportPolicy Exportable `
    -NotAfter (Get-Date).AddYears($ValidYears)
}

# Trust it: export once, import into Root (chain) + TrustedPublisher (auto-load)
$tmp = Join-Path $env:TEMP "xunitrevit-signing.cer"
Export-Certificate -Cert $cert -FilePath $tmp | Out-Null
try {
  Import-Certificate -FilePath $tmp -CertStoreLocation Cert:\CurrentUser\Root | Out-Null
  Import-Certificate -FilePath $tmp -CertStoreLocation Cert:\CurrentUser\TrustedPublisher | Out-Null
} finally {
  Remove-Item $tmp -ErrorAction SilentlyContinue
}

Set-Content -Path $ThumbprintFile -Value $cert.Thumbprint -Encoding ASCII
Write-Host "Certificate installed (Root + TrustedPublisher, CurrentUser)." -ForegroundColor Green
Write-Host "Thumbprint: $($cert.Thumbprint)"
Write-Host "Saved to:   $ThumbprintFile"
Write-Host ""
Write-Host "Next: run-revit-tests.ps1 will now sign deployed DLLs automatically." -ForegroundColor Green
