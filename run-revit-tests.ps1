#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Automated Revit test runner - builds, deploys, launches Revit, waits for results.

.PARAMETER RevitVersion
  Target Revit version (2025, 2026, 2027). Default: 2026.

.PARAMETER Timeout
  Max seconds to wait for Revit to finish tests. Default: 300 (5 min).

.PARAMETER SkipBuild
  Skip build and deploy steps (use existing deployed DLLs).

.EXAMPLE
  .\run-revit-tests.ps1
  .\run-revit-tests.ps1 -RevitVersion 2025 -Timeout 600
  .\run-revit-tests.ps1 -SkipBuild
#>
param(
  [string]$RevitVersion = "2026",
  [int]$Timeout = 300,
  [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$RepoRoot = $PSScriptRoot
$AddinsDir = "$env:APPDATA\Autodesk\REVIT\Addins\$RevitVersion"
$PluginDir = "$AddinsDir\xUnitRevit"
$ResultsPath = "$RepoRoot\RevitTestResults.xml"
$LogPath = "$RepoRoot\RevitTestResults.log"
$RevitExe = "C:\Program Files\Autodesk\Revit $RevitVersion\Revit.exe"

Write-Host "=== xUnitRevit Automated Test Runner ===" -ForegroundColor Cyan
Write-Host "Revit version: $RevitVersion"
Write-Host "Timeout:       $Timeout seconds"
Write-Host ""

# --- Step 1: Kill any existing Revit ---
$existing = Get-Process -Name "Revit" -ErrorAction SilentlyContinue
if ($existing) {
  Write-Host "Killing existing Revit process..." -ForegroundColor Yellow
  Stop-Process -Name "Revit" -Force -ErrorAction SilentlyContinue
  Stop-Process -Name "RevitWorker" -Force -ErrorAction SilentlyContinue
  Start-Sleep -Seconds 2
}

# --- Step 2: Build ---
if (-not $SkipBuild) {
  Write-Host "Building xUnitRevit.Modern (Debug$RevitVersion)..." -ForegroundColor Cyan
  dotnet build "$RepoRoot\xUnitRevit.Modern\xUnitRevit.Modern.csproj" -c "Debug$RevitVersion" --nologo -v q
  if ($LASTEXITCODE -ne 0) { Write-Error "Build failed for xUnitRevit.Modern"; exit 1 }

  Write-Host "Building SampleLibrary.Modern..." -ForegroundColor Cyan
  dotnet build "$RepoRoot\SampleLibrary.Modern\SampleLibrary.Modern.csproj" -c Debug --nologo -v q
  if ($LASTEXITCODE -ne 0) { Write-Error "Build failed for SampleLibrary.Modern"; exit 1 }

  # --- Step 3: Deploy ---
  Write-Host "Deploying to $PluginDir..." -ForegroundColor Cyan
  if (-not (Test-Path $PluginDir)) { New-Item -ItemType Directory -Path $PluginDir -Force | Out-Null }

  # Copy xUnitRevit.Modern build output
  Copy-Item "$RepoRoot\xUnitRevit.Modern\bin\Debug$RevitVersion\net8.0-windows\*" $PluginDir -Recurse -Force

  # Copy SampleLibrary.Modern DLL
  Copy-Item "$RepoRoot\SampleLibrary.Modern\bin\Debug\net8.0-windows\SampleLibrary.Modern.dll" $PluginDir -Force
  Copy-Item "$RepoRoot\SampleLibrary.Modern\bin\Debug\net8.0-windows\SampleLibrary.Modern.pdb" $PluginDir -Force -ErrorAction SilentlyContinue

  # Copy addin manifest
  Copy-Item "$RepoRoot\xUnitRevit.Modern\xUnitRevit.addin" "$AddinsDir\xUnitRevit.addin" -Force

  Write-Host "Deploy complete." -ForegroundColor Green
}

# --- Step 4: Write config ---
$config = @{
  startupAssemblies = @("$PluginDir\SampleLibrary.Modern.dll")
  autoStart = $true
  headless = $true
  resultFormat = "junit"
  resultPath = $ResultsPath
  exitAfterTests = $true
} | ConvertTo-Json
Set-Content "$PluginDir\config.json" $config -Encoding UTF8

# --- Step 5: Clean previous results ---
Remove-Item $ResultsPath -ErrorAction SilentlyContinue
Remove-Item $LogPath -ErrorAction SilentlyContinue

# --- Step 6: Launch Revit ---
if (-not (Test-Path $RevitExe)) {
  Write-Error "Revit not found at: $RevitExe"
  exit 1
}

Write-Host "Launching Revit $RevitVersion..." -ForegroundColor Cyan
$process = Start-Process $RevitExe -PassThru

# --- Step 7: Wait for results ---
Write-Host "Waiting for test results (timeout: ${Timeout}s)..." -ForegroundColor Cyan
$elapsed = 0
$pollInterval = 5

while ($elapsed -lt $Timeout) {
  Start-Sleep -Seconds $pollInterval
  $elapsed += $pollInterval

  # Check if Revit exited/crashed
  if ($process.HasExited) {
    # If results exist, tests completed before crash - that's OK
    if (Test-Path $ResultsPath) {
      Write-Host "Revit exited (code $($process.ExitCode)) but results exist - tests completed." -ForegroundColor Yellow
    } else {
      Write-Host "Revit exited with code $($process.ExitCode) after ${elapsed}s" -ForegroundColor Yellow
    }
    break
  }

  # Check if .done sentinel appeared (tests finished cleanly)
  $sentinelPath = [System.IO.Path]::ChangeExtension($ResultsPath, ".done")
  if (Test-Path $sentinelPath) {
    Write-Host "Tests completed after ${elapsed}s" -ForegroundColor Green
    Remove-Item $sentinelPath -ErrorAction SilentlyContinue
    break
  }

  # Fallback: check if results file appeared (in case sentinel wasn't written)
  if ((Test-Path $ResultsPath) -and $elapsed -gt 15) {
    Start-Sleep -Seconds 2
    Write-Host "Results file detected after ${elapsed}s" -ForegroundColor Green
    break
  }

  # Progress
  if ($elapsed % 30 -eq 0) {
    Write-Host "  Still waiting... (${elapsed}s elapsed)"
  }
}

# --- Step 8: Timeout handling ---
if ($elapsed -ge $Timeout -and -not (Test-Path $ResultsPath)) {
  Write-Host "TIMEOUT after ${Timeout}s - killing Revit" -ForegroundColor Red
  Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
  exit 1
}

# Close Revit gracefully if still running
if (-not $process.HasExited) {
  Write-Host "Closing Revit..." -ForegroundColor Yellow
  # Try graceful close first (WM_CLOSE), then force after 10s
  $process.CloseMainWindow() | Out-Null
  if (-not $process.WaitForExit(10000)) {
    Write-Host "Graceful close timed out, forcing..." -ForegroundColor Yellow
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
  }
}

# --- Step 9: Report results ---
Write-Host ""
Write-Host "=== TEST RESULTS ===" -ForegroundColor Cyan

if (Test-Path $LogPath) {
  # Extract summary line from log
  $summary = Get-Content $LogPath | Where-Object { $_ -match "=== RESULTS:" }
  if ($summary) {
    Write-Host $summary -ForegroundColor White
  }
}

if (Test-Path $ResultsPath) {
  [xml]$xml = Get-Content $ResultsPath
  $root = $xml.testsuites
  $total = [int]$root.tests
  $failures = [int]$root.failures
  $skipped = [int]$root.skipped
  $passed = $total - $failures - $skipped
  $time = $root.time

  Write-Host ""
  Write-Host "Total:   $total" -ForegroundColor White
  Write-Host "Passed:  $passed" -ForegroundColor Green
  $failColor = if ($failures -gt 0) { "Red" } else { "Green" }
  Write-Host "Failed:  $failures" -ForegroundColor $failColor
  Write-Host "Skipped: $skipped" -ForegroundColor Yellow
  Write-Host "Time:    ${time}s"
  Write-Host ""
  Write-Host "JUnit XML: $ResultsPath"
  Write-Host "Log:       $LogPath"

  # List failures
  if ($failures -gt 0) {
    Write-Host ""
    Write-Host "FAILURES:" -ForegroundColor Red
    foreach ($suite in $xml.testsuites.testsuite) {
      foreach ($tc in $suite.testcase) {
        if ($tc.failure) {
          Write-Host "  FAIL $($tc.name)" -ForegroundColor Red
          Write-Host "       $($tc.failure.message)" -ForegroundColor DarkRed
        }
      }
    }
  }

  if ($failures -gt 0) { exit 1 } else { exit 0 }
} else {
  Write-Host "ERROR: No results file found at $ResultsPath" -ForegroundColor Red
  exit 1
}
