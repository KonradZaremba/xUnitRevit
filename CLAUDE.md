# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Is

xUnitRevit is an xUnit test runner that executes tests **inside Autodesk Revit**. It has two execution modes: a WPF UI runner for interactive development, and a headless mode for CI/CD that outputs JUnit XML. Originally by Speckle Systems, this fork adds .NET 8 / Revit 2025-2027 support and headless CI capabilities.

## Build Commands

Build targets a specific Revit version via configuration name (not a generic `Debug`/`Release`):

```bash
# Modern (.NET 8, Revit 2025-2027)
dotnet build xUnitRevit.Modern/xUnitRevit.Modern.csproj -c Debug2026

# Sample test library
dotnet build SampleLibrary.Modern/SampleLibrary.Modern.csproj -c Debug

# Legacy (.NET Framework, Revit 2021-2024) — requires Visual Studio / MSBuild
msbuild xUnitRevit.sln /p:Configuration=Debug2023

# Console runner (no Revit needed, for pipeline validation)
dotnet run --project xUnitRevit.Headless.Console -- MyTests.dll TestResults.xml
```

Valid configurations: `Debug2021`–`Debug2027`, `Release2021`–`Release2027`. Post-build automatically copies output to `%appdata%\Autodesk\Revit\Addins\<version>\`.

## Running Tests

Tests run **inside Revit**, not via `dotnet test`. Two approaches:

1. **Automated script** (builds, deploys, launches Revit, waits for results):
   ```powershell
   ./run-revit-tests.ps1 -RevitVersion 2026
   ./run-revit-tests.ps1 -RevitVersion 2025 -Timeout 600
   ./run-revit-tests.ps1 -SkipBuild  # use already-deployed DLLs
   ```

2. **Manual**: Set `headless: true` in `config.json` (next to xUnitRevit DLL in Addins folder), then launch Revit. Results written as JUnit XML to `resultPath`.

The console runner (`xUnitRevit.Headless.Console`) can run non-Revit tests without Revit installed.

## Architecture

### Multi-version strategy
- **Legacy** (`xUnitRevit/`): .NET Framework 4.8, Revit 2021-2024. Uses `speckle.xunit.runner.wpf` and `ModPlus.Revit.API`.
- **Modern** (`xUnitRevit.Modern/`): .NET 8, Revit 2025-2027. Uses `Autodesk.Revit.SDK` and `CommunityToolkit.Mvvm`.
- Version selection is purely via build configuration — no runtime branching.

### Thread model (critical for understanding bugs)
- **UI mode**: `ExternalEvent` + action queue serializes Revit API calls onto the main thread. Tests discovered/run via `speckle.xunit.runner.wpf`.
- **Headless mode**: `BlockingCollection<T>` work queue + `ManualResetEventSlim` dispatches Revit API calls from background test threads to the main thread via `OnIdling`. Tests that need document access use `xru.DispatchToMainThread()`.
- Revit API is **not thread-safe** — all document/element access must go through the main thread dispatch mechanism.

### Key classes
- `xru` (`xUnitRevitUtils/xru.cs`): Static utility providing `OpenDoc()`, `RunInTransaction()`, `DispatchToMainThread()`. Central to all test authoring.
- `HeadlessRunner` (`xUnitRevit.Modern/HeadlessRunner.cs`): Background test execution + JUnit XML output. Uses `XunitFrontController` directly.
- `App` (`xUnitRevit.Modern/App.cs`): Revit `IExternalApplication` entry point. Routes to UI or headless mode based on `config.json`.
- `Configuration` / `config.json`: Runtime settings — `startupAssemblies`, `headless`, `autoStart`, `resultPath`, `exitAfterTests`.

### Test authoring patterns
- Tests reference `xru` for Revit context: `xru.OpenDoc(path)`, `xru.RunInTransaction(action, doc)`
- Tests should check `xru.IsHeadless` and skip document operations when running headless (no UI thread available for dispatching in pure headless)
- `TestModelLocator` searches up to 6 parent directories for a `TestModels/` folder
- xUnit fixtures (`IClassFixture<T>`) are used to share open documents across tests

## CI/CD

- Azure Pipelines (`azure-pipelines.yml`): matrix build across all 7 Revit versions
- NuGet publish of `xUnitRevitUtils.<version>` on tag push
- JUnit XML output is compatible with GitHub Actions, Azure Pipelines, Jenkins

## Config

Copy `config_sample.json` to `config.json` next to the xUnitRevit DLL. Do **not** commit `config.json`.
