#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Run Revit tests on a local or remote machine.

.DESCRIPTION
  Wraps run-revit-tests.ps1 for local execution or PowerShell remoting to a VM.
  For local: runs directly (same as run-revit-tests.ps1).
  For remote: copies build output, runs tests, copies results back.

.PARAMETER ComputerName
  Target machine. Default: localhost (runs locally).

.PARAMETER RevitVersion
  Revit version (2025, 2026, 2027). Default: 2026.

.PARAMETER Timeout
  Max seconds to wait. Default: 600.

.PARAMETER SkipBuild
  Skip local build step.

.PARAMETER Credential
  PSCredential for remote machine. Prompted if ComputerName is not localhost.

.PARAMETER SshTarget
  SSH target (user@host) for SSH-based remoting instead of WinRM.

.EXAMPLE
  # Local
  .\run-revit-tests-remote.ps1

  # Remote via WinRM
  .\run-revit-tests-remote.ps1 -ComputerName "revit-vm" -Credential (Get-Credential)

  # Remote via SSH
  .\run-revit-tests-remote.ps1 -SshTarget "user@revit-vm.local"
#>
param(
  [string]$ComputerName = "localhost",
  [string]$RevitVersion = "2026",
  [int]$Timeout = 600,
  [switch]$SkipBuild,
  [PSCredential]$Credential,
  [string]$SshTarget
)

$ErrorActionPreference = "Stop"
$RepoRoot = $PSScriptRoot
$ResultsXml = "$RepoRoot\RevitTestResults.xml"
$ResultsLog = "$RepoRoot\RevitTestResults.log"

# --- Local execution ---
if (-not $SshTarget -and ($ComputerName -eq "localhost" -or $ComputerName -eq $env:COMPUTERNAME)) {
  Write-Host "Running locally..." -ForegroundColor Cyan
  $args = @("-RevitVersion", $RevitVersion, "-Timeout", $Timeout)
  if ($SkipBuild) { $args += "-SkipBuild" }
  & "$RepoRoot\run-revit-tests.ps1" @args
  exit $LASTEXITCODE
}

# --- Build locally first ---
if (-not $SkipBuild) {
  Write-Host "Building locally..." -ForegroundColor Cyan
  dotnet build "$RepoRoot\xUnitRevit.Modern\xUnitRevit.Modern.csproj" -c "Debug$RevitVersion" --nologo -v q
  if ($LASTEXITCODE -ne 0) { Write-Error "Build failed"; exit 1 }

  dotnet build "$RepoRoot\SampleLibrary.Modern\SampleLibrary.Modern.csproj" -c Debug --nologo -v q
  if ($LASTEXITCODE -ne 0) { Write-Error "Build failed"; exit 1 }
}

$BuildOutput = "$RepoRoot\xUnitRevit.Modern\bin\Debug$RevitVersion\net8.0-windows"
$TestDll = "$RepoRoot\SampleLibrary.Modern\bin\Debug\net8.0-windows\SampleLibrary.Modern.dll"
$TestModelsDir = "$RepoRoot\SampleLibrary.Modern\bin\Debug\net8.0-windows\TestModels"

# --- SSH-based remoting ---
if ($SshTarget) {
  Write-Host "Deploying to $SshTarget via SSH..." -ForegroundColor Cyan

  $RemoteDir = "C:\temp\xUnitRevitTests"
  $RemoteAddins = "C:\Users\$($SshTarget.Split('@')[0])\AppData\Roaming\Autodesk\REVIT\Addins\$RevitVersion\xUnitRevit"

  # Copy files
  ssh $SshTarget "mkdir -p '$RemoteDir' '$RemoteAddins' 2>NUL"
  scp -r "$BuildOutput/*" "${SshTarget}:${RemoteAddins}/"
  scp "$TestDll" "${SshTarget}:${RemoteAddins}/"
  scp -r "$TestModelsDir" "${SshTarget}:${RemoteAddins}/"
  scp "$RepoRoot\xUnitRevit.Modern\xUnitRevit.addin" "${SshTarget}:C:\Users\$($SshTarget.Split('@')[0])\AppData\Roaming\Autodesk\REVIT\Addins\$RevitVersion\xUnitRevit.addin"
  scp "$RepoRoot\run-revit-tests.ps1" "${SshTarget}:${RemoteDir}/"

  # Run tests
  Write-Host "Running tests on $SshTarget..." -ForegroundColor Cyan
  ssh $SshTarget "powershell -ExecutionPolicy Bypass -File '$RemoteDir\run-revit-tests.ps1' -RevitVersion $RevitVersion -Timeout $Timeout -SkipBuild"
  $exitCode = $LASTEXITCODE

  # Copy results back
  scp "${SshTarget}:${RemoteDir}\RevitTestResults.xml" $ResultsXml 2>$null
  scp "${SshTarget}:${RemoteDir}\RevitTestResults.log" $ResultsLog 2>$null

  if (Test-Path $ResultsXml) {
    Write-Host "Results copied to $ResultsXml" -ForegroundColor Green
  }
  exit $exitCode
}

# --- WinRM-based remoting ---
Write-Host "Deploying to $ComputerName via WinRM..." -ForegroundColor Cyan

if (-not $Credential) {
  $Credential = Get-Credential -Message "Credentials for $ComputerName"
}

$session = New-PSSession -ComputerName $ComputerName -Credential $Credential

try {
  $RemoteDir = "C:\temp\xUnitRevitTests"
  Invoke-Command -Session $session -ScriptBlock {
    param($dir)
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
  } -ArgumentList $RemoteDir

  # Copy build output and script
  Copy-Item -ToSession $session -Path "$BuildOutput\*" -Destination $RemoteDir -Recurse -Force
  Copy-Item -ToSession $session -Path $TestDll -Destination $RemoteDir -Force
  Copy-Item -ToSession $session -Path $TestModelsDir -Destination "$RemoteDir\TestModels" -Recurse -Force
  Copy-Item -ToSession $session -Path "$RepoRoot\run-revit-tests.ps1" -Destination $RemoteDir -Force
  Copy-Item -ToSession $session -Path "$RepoRoot\xUnitRevit.Modern\xUnitRevit.addin" -Destination $RemoteDir -Force

  # Run tests remotely
  Write-Host "Running tests on $ComputerName..." -ForegroundColor Cyan
  $result = Invoke-Command -Session $session -ScriptBlock {
    param($dir, $version, $timeout)
    Set-Location $dir
    & "$dir\run-revit-tests.ps1" -RevitVersion $version -Timeout $timeout -SkipBuild
    return $LASTEXITCODE
  } -ArgumentList $RemoteDir, $RevitVersion, $Timeout

  # Copy results back
  Copy-Item -FromSession $session -Path "$RemoteDir\RevitTestResults.xml" -Destination $ResultsXml -ErrorAction SilentlyContinue
  Copy-Item -FromSession $session -Path "$RemoteDir\RevitTestResults.log" -Destination $ResultsLog -ErrorAction SilentlyContinue

  if (Test-Path $ResultsXml) {
    Write-Host "Results copied to $ResultsXml" -ForegroundColor Green
  }

  exit $result
}
finally {
  Remove-PSSession $session
}
