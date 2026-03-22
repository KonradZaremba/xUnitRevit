using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;
using Xunit;
using xUnitRevitUtils;

namespace SampleLibrary.Modern
{
  /// <summary>
  /// Resolves test model paths across execution contexts (Revit headless, Revit UI, console runner).
  /// </summary>
  internal static class TestModelLocator
  {
    internal static string GetTestModel(string filename)
    {
      // Search candidate directories for the TestModels folder
      var candidates = new[]
      {
        // 1. Relative to test assembly location (works inside Revit where DLL is deployed)
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
        // 2. Current working directory (works in console runner / dotnet test)
        Directory.GetCurrentDirectory(),
      };

      foreach (var baseDir in candidates)
      {
        if (string.IsNullOrEmpty(baseDir)) continue;

        // Walk up from baseDir looking for a TestModels folder
        var dir = baseDir;
        for (int i = 0; i < 6; i++)
        {
          var testModelsDir = Path.Combine(dir, "TestModels");
          var fullPath = Path.Combine(testModelsDir, filename);
          if (File.Exists(fullPath))
            return Path.GetFullPath(fullPath);

          // Also check SampleLibrary/TestModels (repo layout)
          var sampleLibDir = Path.Combine(dir, "SampleLibrary", "TestModels");
          fullPath = Path.Combine(sampleLibDir, filename);
          if (File.Exists(fullPath))
            return Path.GetFullPath(fullPath);

          var parent = Path.GetDirectoryName(dir);
          if (parent == null || parent == dir) break;
          dir = parent;
        }
      }

      // Fallback: return the old-style path so the error message is meaningful
      return Path.Combine(Directory.GetCurrentDirectory(), "TestModels", filename);
    }
  }

  /// <summary>
  /// Tests that exercise Revit API commands.
  /// These require Revit to be running (UI mode or headless via Revit Platform Services).
  /// </summary>
  public class RevitDocumentTests
  {
    [Fact]
    public void ApplicationIsInitialized()
    {
      // Verify that xru was properly initialized with a Revit Application
      Assert.NotNull(xru.App);
    }

    [Fact]
    public void CanOpenDocument()
    {
      var testModel = TestModelLocator.GetTestModel("walls.rvt");
      var doc = xru.OpenDoc(testModel);

      Assert.NotNull(doc);
      Assert.False(doc.IsFamilyDocument);
      Assert.NotNull(doc.Title);
    }

    [Fact]
    public void DocumentHasActiveView()
    {
      var testModel = TestModelLocator.GetTestModel("walls.rvt");
      var doc = xru.OpenDoc(testModel);
      var activeView = doc.ActiveView;

      Assert.NotNull(activeView);
      Assert.NotEqual(ViewType.Undefined, activeView.ViewType);
    }

  }

  /// <summary>
  /// Tests querying elements with FilteredElementCollector.
  /// </summary>
  public class ElementCollectorTests
  {
    [Fact]
    public void CollectAllWalls()
    {
      var testModel = TestModelLocator.GetTestModel("walls.rvt");
      var doc = xru.OpenDoc(testModel);

      var walls = new FilteredElementCollector(doc)
        .WhereElementIsNotElementType()
        .OfCategory(BuiltInCategory.OST_Walls)
        .ToElements();

      Assert.NotEmpty(walls);
      Assert.All(walls, w => Assert.IsAssignableFrom<Wall>(w));
    }

    [Fact]
    public void WallsHaveValidVolume()
    {
      var testModel = TestModelLocator.GetTestModel("walls.rvt");
      var doc = xru.OpenDoc(testModel);

      var walls = new FilteredElementCollector(doc)
        .WhereElementIsNotElementType()
        .OfCategory(BuiltInCategory.OST_Walls)
        .ToElements();

      foreach (var wall in walls)
      {
        var volumeParam = wall.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED);
        Assert.NotNull(volumeParam);
        Assert.True(volumeParam.AsDouble() > 0, $"Wall {wall.Id} has zero or negative volume");
      }
    }

    [Fact]
    public void CollectWallTypes()
    {
      var testModel = TestModelLocator.GetTestModel("walls.rvt");
      var doc = xru.OpenDoc(testModel);

      var wallTypes = new FilteredElementCollector(doc)
        .WhereElementIsElementType()
        .OfCategory(BuiltInCategory.OST_Walls)
        .Cast<WallType>()
        .ToList();

      Assert.NotEmpty(wallTypes);
      Assert.All(wallTypes, wt => Assert.NotNull(wt.Name));
    }

    [Fact]
    public void CollectLevels()
    {
      var testModel = TestModelLocator.GetTestModel("walls.rvt");
      var doc = xru.OpenDoc(testModel);

      var levels = new FilteredElementCollector(doc)
        .OfClass(typeof(Level))
        .Cast<Level>()
        .OrderBy(l => l.Elevation)
        .ToList();

      Assert.NotEmpty(levels);
      // Verify levels are ordered by elevation
      for (int i = 1; i < levels.Count; i++)
      {
        Assert.True(levels[i].Elevation >= levels[i - 1].Elevation,
          $"Level {levels[i].Name} elevation ({levels[i].Elevation}) should be >= {levels[i - 1].Name} ({levels[i - 1].Elevation})");
      }
    }

    [Fact]
    public void CollectViewsInDocument()
    {
      var testModel = TestModelLocator.GetTestModel("walls.rvt");
      var doc = xru.OpenDoc(testModel);

      var views = new FilteredElementCollector(doc)
        .OfClass(typeof(View))
        .Cast<View>()
        .Where(v => !v.IsTemplate)
        .ToList();

      Assert.NotEmpty(views);
    }

  }

  /// <summary>
  /// Tests that use transactions to modify and roll back changes.
  /// </summary>
  public class TransactionTests
  {
    [Fact]
    public void CreateAndDeleteWall()
    {
      var testModel = TestModelLocator.GetTestModel("walls.rvt");
      var doc = xru.OpenDoc(testModel);

      int wallCountBefore = new FilteredElementCollector(doc)
        .WhereElementIsNotElementType()
        .OfCategory(BuiltInCategory.OST_Walls)
        .GetElementCount();

      // Create a wall inside a transaction, then roll back
      xru.Run(() =>
      {
        using (Transaction t = new Transaction(doc, "Test - Create Wall"))
        {
          t.Start();

          // Get a level to place the wall on
          var level = new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .First();

          // Create a simple line for the wall
          var start = new XYZ(0, 0, 0);
          var end = new XYZ(20, 0, 0); // 20 feet long
          var line = Line.CreateBound(start, end);

          var wall = Wall.Create(doc, line, level.Id, false);
          Assert.NotNull(wall);
          Assert.True(wall.Id != ElementId.InvalidElementId);

          // Roll back — don't keep the wall
          t.RollBack();
        }
      }, doc).Wait();

      // Verify wall count is unchanged after rollback
      int wallCountAfter = new FilteredElementCollector(doc)
        .WhereElementIsNotElementType()
        .OfCategory(BuiltInCategory.OST_Walls)
        .GetElementCount();

      Assert.Equal(wallCountBefore, wallCountAfter);
    }

    [Fact]
    public void ModifyWallParameterAndRollBack()
    {
      var testModel = TestModelLocator.GetTestModel("walls.rvt");
      var doc = xru.OpenDoc(testModel);

      var wall = new FilteredElementCollector(doc)
        .WhereElementIsNotElementType()
        .OfCategory(BuiltInCategory.OST_Walls)
        .FirstElement() as Wall;

      Assert.NotNull(wall);

      var offsetParam = wall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET);
      double originalOffset = offsetParam.AsDouble();

      // Modify and roll back
      xru.Run(() =>
      {
        using (Transaction t = new Transaction(doc, "Test - Modify Offset"))
        {
          t.Start();
          offsetParam.Set(originalOffset + 5.0); // Add 5 feet
          Assert.Equal(originalOffset + 5.0, offsetParam.AsDouble(), precision: 5);
          t.RollBack();
        }
      }, doc).Wait();

      // Verify parameter reverted
      Assert.Equal(originalOffset, offsetParam.AsDouble(), precision: 5);
    }

    [Fact]
    public void RunInTransactionHelper()
    {
      var testModel = TestModelLocator.GetTestModel("walls.rvt");
      var doc = xru.OpenDoc(testModel);

      var walls = new FilteredElementCollector(doc)
        .WhereElementIsNotElementType()
        .OfCategory(BuiltInCategory.OST_Walls)
        .ToElements();

      // Use the xru.RunInTransaction helper
      xru.RunInTransaction(() =>
      {
        foreach (var wall in walls)
        {
          var param = wall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET);
          var offset = UnitUtils.ConvertToInternalUnits(500, param.GetUnitTypeId());
          param.Set(offset);
        }
      }, doc, "Set Wall Offsets").Wait();

      // Verify changes were committed
      foreach (var wall in walls)
      {
        var param = wall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET);
        Assert.True(param.AsDouble() > 0, $"Wall {wall.Id} offset should be > 0 after transaction");
      }
    }

  }

  /// <summary>
  /// Tests for geometry extraction from Revit elements.
  /// </summary>
  public class GeometryExtractionTests
  {
    [Fact]
    public void WallHasSolid()
    {
      var testModel = TestModelLocator.GetTestModel("walls.rvt");
      var doc = xru.OpenDoc(testModel);

      var wall = new FilteredElementCollector(doc)
        .WhereElementIsNotElementType()
        .OfCategory(BuiltInCategory.OST_Walls)
        .FirstElement();

      Assert.NotNull(wall);

      var options = new Options { ComputeReferences = true };
      var geomElement = wall.get_Geometry(options);
      Assert.NotNull(geomElement);

      var solids = geomElement
        .OfType<Solid>()
        .Where(s => s.Volume > 0)
        .ToList();

      Assert.NotEmpty(solids);
      Assert.All(solids, s =>
      {
        Assert.True(s.Volume > 0, "Solid volume should be positive");
        Assert.True(s.SurfaceArea > 0, "Solid surface area should be positive");
      });
    }

    [Fact]
    public void WallLocationIsLine()
    {
      var testModel = TestModelLocator.GetTestModel("walls.rvt");
      var doc = xru.OpenDoc(testModel);

      var wall = new FilteredElementCollector(doc)
        .WhereElementIsNotElementType()
        .OfCategory(BuiltInCategory.OST_Walls)
        .FirstElement() as Wall;

      Assert.NotNull(wall);

      var location = wall.Location as LocationCurve;
      Assert.NotNull(location);
      Assert.IsType<Line>(location.Curve);

      var line = location.Curve as Line;
      Assert.True(line.Length > 0, "Wall length should be positive");
    }

    [Fact]
    public void WallBoundingBoxIsValid()
    {
      var testModel = TestModelLocator.GetTestModel("walls.rvt");
      var doc = xru.OpenDoc(testModel);

      var wall = new FilteredElementCollector(doc)
        .WhereElementIsNotElementType()
        .OfCategory(BuiltInCategory.OST_Walls)
        .FirstElement();

      Assert.NotNull(wall);

      var bb = wall.get_BoundingBox(null);
      Assert.NotNull(bb);
      Assert.True(bb.Max.X >= bb.Min.X, "BoundingBox Max.X should be >= Min.X");
      Assert.True(bb.Max.Y >= bb.Min.Y, "BoundingBox Max.Y should be >= Min.Y");
      Assert.True(bb.Max.Z >= bb.Min.Z, "BoundingBox Max.Z should be >= Min.Z");
    }

  }

  /// <summary>
  /// Tests for unit conversion (Revit 2021+ API).
  /// </summary>
  public class UnitConversionTests
  {
    [Theory]
    [InlineData(1000, 3.28084)]
    [InlineData(2000, 6.56168)]
    [InlineData(3000, 9.84252)]
    public void MillimetersToFeetConversion(double mm, double expectedFeet)
    {
      double feet = UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
      Assert.Equal(expectedFeet, feet, precision: 3);
    }

    [Theory]
    [InlineData(1.0, 304.8)]
    [InlineData(2.0, 609.6)]
    [InlineData(10.0, 3048.0)]
    public void FeetToMillimetersConversion(double feet, double expectedMm)
    {
      double mm = UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters);
      Assert.Equal(expectedMm, mm, precision: 1);
    }

    [Fact]
    public void SquareMetersConversion()
    {
      // 10 sq feet to sq meters
      double sqFeet = 10.0;
      double sqMeters = UnitUtils.ConvertFromInternalUnits(sqFeet, UnitTypeId.SquareMeters);
      Assert.True(sqMeters > 0);
      Assert.True(sqMeters < sqFeet); // sq meters < sq feet always
    }

    [Fact]
    public void CubicMetersConversion()
    {
      double cubicFeet = 100.0;
      double cubicMeters = UnitUtils.ConvertFromInternalUnits(cubicFeet, UnitTypeId.CubicMeters);
      Assert.True(cubicMeters > 0);
      Assert.True(cubicMeters < cubicFeet);
    }
  }
}
