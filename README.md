# xUnitRevit

[![Build Status](https://teocomi.visualstudio.com/Speckle/_apis/build/status/Speckle-Next.xunit-Revit?branchName=master)](https://teocomi.visualstudio.com/Speckle/_build/latest?definitionId=2&branchName=master)

![xunit2](https://user-images.githubusercontent.com/2679513/88958499-77809980-d298-11ea-84b6-e0749790ffc5.gif)

[![Twitter Follow](https://img.shields.io/twitter/follow/SpeckleSystems?style=social)](https://twitter.com/SpeckleSystems) [![Community forum users](https://img.shields.io/discourse/users?server=https%3A%2F%2Fdiscourse.speckle.works&style=flat-square&logo=discourse&logoColor=white)](https://discourse.speckle.works) [![website](https://img.shields.io/badge/https://-speckle.systems-royalblue?style=flat-square)](https://speckle.systems) [![docs](https://img.shields.io/badge/docs-speckle.guide-orange?style=flat-square&logo=read-the-docs&logoColor=white)](https://speckle.guide/dev/)

> **New here? See [QUICKSTART.md](QUICKSTART.md)** for a step-by-step setup + testing guide (signing, console runner, headless, in-Revit UI).

## Introduction

An xUnit runner for Autodesk Revit.

Check out our blog post on this 👉 https://speckle.systems/blog/xunitrevit !

xUnitRevit uses [speckle.xunit.runner.wpf](https://github.com/Speckle-Next/speckle.xunit.runner.wpf) which is a fork of [xunit.runner.wpf](https://github.com/Pilchie/xunit.runner.wpf), it allows to easily develop and run xUnit tests in Revit.

Many thanks to all the developers of xunit and xunit.runner.wpf!

### Structure

This repo is composed of the following projects:

**Modern (.NET 8 — Revit 2025+):**
- **xUnitRevit.Modern**: Revit addin with UI runner and headless mode for CI/CD
- **xUnitRevitUtils**: shared utility library (`xru` static class) for both UI and headless modes
- **SampleLibrary.Modern**: sample tests — basic assertions + Revit API tests
- **xUnitRevit.Headless.Console**: standalone console runner for testing the pipeline without Revit

**Legacy (.NET Framework — Revit 2021–2023):**
- **xUnitRevit**: the original Revit addin (WPF UI runner)



## Getting Started

### Modern (Revit 2025+ / .NET 8)

1. Copy `xUnitRevit.Modern/config_sample.json` to `config.json`
2. Build with the configuration matching your Revit version:
   ```
   dotnet build xUnitRevit.Modern/xUnitRevit.Modern.csproj -c Debug2026
   ```
   Available configurations: `Debug2025`, `Debug2026`, `Debug2027`
3. The post-build step copies DLLs and `.addin` manifest to `%appdata%\Autodesk\Revit\Addins\<version>\`
4. Create a test library targeting `net8.0-windows` with references to `xunit` and `xUnitRevitUtils`
5. Add your test DLL path to `config.json` → `startupAssemblies`
6. Start Revit — use the UI runner or enable headless mode

#### Headless / CI mode

Set `headless: true` in `config.json` to run tests automatically on Revit startup and output JUnit XML results:

```json
{
  "startupAssemblies": ["C:\\path\\to\\MyTests.dll"],
  "autoStart": true,
  "headless": true,
  "resultFormat": "junit",
  "resultPath": "C:\\output\\TestResults.xml"
}
```

Tests run on a background thread. Revit API calls (document opens, element queries, transactions) are dispatched to the main thread via the `Idling` event — document tests work fully in headless mode. Results are written as JUnit XML, compatible with GitHub Actions, Azure Pipelines, and Jenkins.

#### Code signing (skips Revit's "unsigned add-in" prompt)

Revit prompts to trust unsigned add-ins, and the add-in cannot dismiss its **own** load prompt. Run this once per machine to create and trust a local self-signed cert (no admin, no purchase):

```powershell
./setup-signing.ps1
```

Every build and deploy then auto-signs the add-in DLLs (`sign-addin.ps1`, wired into the build, `run-revit-tests.ps1`, and `xunitrevit.targets`). The cert is local-dev only — it does not help on other machines.

#### Automated test runner

Builds, deploys (signed), launches Revit, waits for results, reports, then **resets `config.json` to dormant** so your next manual Revit launch is normal (not hijacked into headless test mode):

```powershell
./run-revit-tests.ps1 -RevitVersion 2026
./run-revit-tests.ps1 -RevitVersion 2025 -Timeout 600
./run-revit-tests.ps1 -SkipBuild  # use already-deployed DLLs
```

The console runner skips `[Trait("Category", "Revit")]` tests by default (`--include-revit` to run them) and returns exit codes `0` (pass), `1` (test failures), `2` (infrastructure failure). The in-Revit runner window shows colored pass/fail/skip badges and a **📋 Copy Report** button that copies a text summary to the clipboard.

#### Per-version test models (faster runs)

Opening the shared older-format `walls.rvt` makes Revit upgrade it in-memory (~28 s) every run. Save a native copy per version — `SampleLibrary/TestModels/walls_2026.rvt` — and `TestModelLocator` prefers `walls_<version>.rvt`, falling back to `walls.rvt`. Native opens are near-instant; older versions still work via the fallback.

#### Console runner (no Revit required)

For testing the pipeline or running non-Revit tests:

```
dotnet run --project xUnitRevit.Headless.Console -- MyTests.dll TestResults.xml
```

### Legacy (Revit 2021–2023 / .NET Framework)

1. Copy `xUnitRevit/config_sample.json` to `config.json`
2. Build in **Debug mode** with the matching build configuration (e.g., `Debug2023`)
3. The post-build step copies DLLs to the Revit addin folder
4. Start Revit, launch xUnitRevit, and select your test library

### Creating a test library

**For Revit 2025+ (.NET 8):**
- Create a `net8.0-windows` class library
- Add NuGet packages: `xunit`, `Autodesk.Revit.SDK`
- Add a project reference to `xUnitRevitUtils`

**For Revit 2021–2023 (.NET Framework):**
- Create a .NET Framework 4.8 class library
- Add NuGet packages: `xunit`, `xUnitRevitUtils.2021` (or `.2022`, `.2023`)

That's it, now we can start adding our tests.

#### Writing a simple test

To do almost anything with the Revit API you need a reference to the active Document, and this is where xUnitRevitUtils comes into play, with its `xru` static class. All Revit API calls must be dispatched to the main thread — `xru.DispatchToMainThread()` handles this in both UI and headless mode.

Full code: [SampleLibrary.Modern/RevitTests.cs](SampleLibrary.Modern/RevitTests.cs)

```csharp
[Collection("Revit")]
public class ElementCollectorTests
{
  private readonly Document _doc;
  public ElementCollectorTests(WallsDocFixture fixture) { _doc = fixture.Doc; }

  [Fact]
  public void WallsHaveValidVolume()
  {
    IList<Element> walls = null;
    xru.DispatchToMainThread(() =>
    {
      walls = new FilteredElementCollector(_doc)
        .WhereElementIsNotElementType()
        .OfCategory(BuiltInCategory.OST_Walls)
        .ToElements();
    });

    foreach (var wall in walls)
    {
      Parameter volumeParam = null;
      xru.DispatchToMainThread(() =>
      {
        volumeParam = wall.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED);
      });
      Assert.NotNull(volumeParam);
      Assert.True(volumeParam.AsDouble() > 0);
    }
  }
}
```

#### Writing tests with fixtures

To share context between tests (e.g. open a Revit model once), use xUnit [collection fixtures](https://xunit.net/docs/shared-context). All Revit test classes must share a `[Collection]` so they run sequentially — parallel document opens crash Revit.

Full code: [SampleLibrary.Modern/RevitTests.cs](SampleLibrary.Modern/RevitTests.cs)

```csharp
// Fixture: opens the document once, shared across all test classes in the collection
public class WallsDocFixture : IDisposable
{
  public Document Doc { get; }

  public WallsDocFixture()
  {
    var testModel = TestModelLocator.GetTestModel("walls.rvt");
    Doc = xru.OpenDoc(testModel);
  }

  public void Dispose() { }
}

// Collection definition — binds the fixture and prevents parallel execution
[CollectionDefinition("Revit")]
public class RevitCollection : ICollectionFixture<WallsDocFixture> { }

// Test class — receives the fixture via constructor injection
[Collection("Revit")]
public class RevitDocumentTests
{
  private readonly WallsDocFixture _fixture;
  public RevitDocumentTests(WallsDocFixture fixture) { _fixture = fixture; }

  [Fact]
  public void CanOpenDocument()
  {
    Assert.NotNull(_fixture.Doc);
    Assert.False(_fixture.Doc.IsFamilyDocument);
  }
}
```

#### Writing tests that use Revit transactions

`xru.RunInTransaction()` wraps your action in a transaction and dispatches it to the main thread. Always `.Wait()` to block until the transaction completes.

```csharp
[Collection("Revit")]
public class TransactionTests
{
  private readonly Document _doc;
  public TransactionTests(WallsDocFixture fixture) { _doc = fixture.Doc; }

  [Fact]
  public void ModifyWallParameterAndRollBack()
  {
    Wall wall = null;
    Parameter offsetParam = null;
    double originalOffset = 0;

    xru.DispatchToMainThread(() =>
    {
      wall = new FilteredElementCollector(_doc)
        .WhereElementIsNotElementType()
        .OfCategory(BuiltInCategory.OST_Walls)
        .FirstElement() as Wall;

      offsetParam = wall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET);
      originalOffset = offsetParam.AsDouble();
    });

    xru.Run(() =>
    {
      using (Transaction t = new Transaction(_doc, "Test - Modify Offset"))
      {
        t.Start();
        offsetParam.Set(originalOffset + 5.0);
        t.RollBack();
      }
    }, _doc).Wait(); // Important! Wait for action to finish

    double finalOffset = 0;
    xru.DispatchToMainThread(() =>
    {
      finalOffset = offsetParam.AsDouble();
    });
    Assert.Equal(originalOffset, finalOffset, precision: 5);
  }
}
```



## Additional Notes

### Configuration

Copy `config_sample.json` to `config.json` (next to the xUnitRevit DLL). Available settings:

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `startupAssemblies` | `string[]` | `[]` | Paths to test DLLs to load on startup |
| `autoStart` | `bool` | `false` | Auto-open the test runner window (UI mode) or auto-run tests (headless) |
| `headless` | `bool` | `false` | Run tests without UI, output results to file |
| `resultFormat` | `string` | `"junit"` | Output format for test results |
| `resultPath` | `string` | `"./TestResults.xml"` | Path for test result output |
| `exitAfterTests` | `bool` | `false` | Write a `.done` sentinel when tests finish (the automation script then closes Revit) |

**Two modes.** With `headless:false, autoStart:false` (the default, and what `run-revit-tests.ps1` leaves behind) the add-in loads but stays idle — Revit works normally and you run tests on demand via **Add-Ins ▸ External Tools ▸ xUnitRevit**. Set `headless:true` only for automated/CI runs; leaving it on makes Revit auto-run tests on every launch.

### Dll locking

DLLs loaded by xUnitRevit are loaded in Revit's AppDomain, and therefore it's not possible to recompile them until Revit is closed (even if you see an auto reload option in the UI). Since Revit 2020 it's possible to *edit & continue* your code while debugging, so you won't have to restart Revit each time.

## Contributing

xUnitRevit was developed to help us develop a better Speckle 2.0 connector for Revit, we hope you'll find it useful too. 

Want to suggest a feature, report a bug, submit a PR? Please open an issue to discuss first! 

Please make sure you read the [contribution guidelines](.github/CONTRIBUTING.md) and [code of conduct](.github/CODE_OF_CONDUCT.md) for an overview of the practices we try to follow.

## Community

The Speckle Community hangs out on [the forum](https://discourse.speckle.works), do join and introduce yourself & feel free to ask us questions!

## Security

For any security vulnerabilities or concerns, please contact us directly at security[at]speckle.systems.

## License

Unless otherwise described, the code in this repository is licensed under the MIT License. Please note that some modules, extensions or code herein might be otherwise licensed. This is indicated either in the root of the containing folder under a different license file, or in the respective file's header. If you have any questions, don't hesitate to get in touch with us via [email](mailto:hello@speckle.systems).