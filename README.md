# xUnitRevit

[![Build Status](https://teocomi.visualstudio.com/Speckle/_apis/build/status/Speckle-Next.xunit-Revit?branchName=master)](https://teocomi.visualstudio.com/Speckle/_build/latest?definitionId=2&branchName=master)

![xunit2](https://user-images.githubusercontent.com/2679513/88958499-77809980-d298-11ea-84b6-e0749790ffc5.gif)

[![Twitter Follow](https://img.shields.io/twitter/follow/SpeckleSystems?style=social)](https://twitter.com/SpeckleSystems) [![Community forum users](https://img.shields.io/discourse/users?server=https%3A%2F%2Fdiscourse.speckle.works&style=flat-square&logo=discourse&logoColor=white)](https://discourse.speckle.works) [![website](https://img.shields.io/badge/https://-speckle.systems-royalblue?style=flat-square)](https://speckle.systems) [![docs](https://img.shields.io/badge/docs-speckle.guide-orange?style=flat-square&logo=read-the-docs&logoColor=white)](https://speckle.guide/dev/)

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

Tests run on a background thread. Results are written as JUnit XML, compatible with GitHub Actions, Azure Pipelines, and Jenkins.

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

To do almost anything with the Revit API you need a reference to the active Document, and this is where xUnitRevitUtils comes into play, with its `xru` static class. The code below shows how we can use it to get a list of Walls and check their properties.

Full code : https://github.com/Speckle-Next/xUnitRevit/blob/master/SampleLibrary/SampleTest.cs

```csharp
  [Fact]
public void WallsHaveVolume()
{
  var testModel = GetTestModel("walls.rvt");
  var doc = xru.OpenDoc(testModel);

  var walls = new FilteredElementCollector(doc).WhereElementIsNotElementType().OfCategory(BuiltInCategory.OST_Walls).ToElements();

  foreach(var wall in walls)
  {
    var volumeParam = wall.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED);
    Assert.NotNull(volumeParam);
    Assert.True(volumeParam.AsDouble() > 0);
  }
  doc.Close(false);
}
```

#### Writing tests with fixtures

To be able to share context between tests, xUnits uses [fixtures](https://xunit.net/docs/shared-context). We can use fixtures for instance, to open a Revit model only once and use it across multiple tests.

Let's see an example, full code: https://github.com/Speckle-Next/xUnitRevit/blob/master/SampleLibrary/TestWithFixture.cs

```csharp
public class DocFixture : IDisposable
{
  public Document Doc { get; set; }
  public IList<Element> Walls { get; set; }


  public DocFixture()
  {
    var testModel = Utils.GetTestModel("walls.rvt");
    Doc = xru.OpenDoc(testModel);

    Walls = new FilteredElementCollector(Doc).WhereElementIsNotElementType().OfCategory(BuiltInCategory.OST_Walls).ToElements();
  }

  public void Dispose()
  {
  }
}
public class TestWithFixture : IClassFixture<DocFixture>
{
  DocFixture fixture; 
  public TestWithFixture(DocFixture fixture)
  {
    this.fixture = fixture;
  }

  [Fact]
  public void CountWalls()
  {
    Assert.Equal(4, fixture.Walls.Count);
  }

  [Fact]
  public void WallOffset()
  {
    var wall = fixture.Doc.GetElement(new ElementId(346573));
    var param = wall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET);
    var baseOffset = UnitUtils.ConvertFromInternalUnits(param.AsDouble(), param.DisplayUnitType);

    Assert.Equal(2000, baseOffset);
  }
}
```

#### Writing test that use Revit transactions

Another feature of xUnitRevitUtils is that it offers a helper method to run Transactions, so you don't have to worry about that 🤯! Check the example below: https://github.com/Speckle-Next/xUnitRevit/blob/master/SampleLibrary/TestWithFixture.cs

```csharp
[Fact]
public void MoveWallsUp()
{
  var walls = fixture.Walls.Where(x => x.Id.IntegerValue != 346573);

  xru.RunInTransaction(() =>
  {
    foreach(var wall in walls)
    {
      var param = wall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET);
      var baseOffset = UnitUtils.ConvertToInternalUnits(2000, param.DisplayUnitType);
      param.Set(baseOffset);
    }
  }, fixture.Doc)
  .Wait(); // Important! Wait for action to finish

  foreach (var wall in walls)
  {
    var param = wall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET);
    var baseOffset = UnitUtils.ConvertFromInternalUnits(param.AsDouble(), param.DisplayUnitType);
    Assert.Equal(2000, baseOffset);
  }
}
```

![image](https://user-images.githubusercontent.com/2679513/88953549-025d9600-d291-11ea-8ec4-58c85c84c5aa.png)



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