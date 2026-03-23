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
      var candidates = new[]
      {
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
        Directory.GetCurrentDirectory(),
      };

      foreach (var baseDir in candidates)
      {
        if (string.IsNullOrEmpty(baseDir)) continue;

        var dir = baseDir;
        for (int i = 0; i < 6; i++)
        {
          var testModelsDir = Path.Combine(dir, "TestModels");
          var fullPath = Path.Combine(testModelsDir, filename);
          if (File.Exists(fullPath))
            return Path.GetFullPath(fullPath);

          var sampleLibDir = Path.Combine(dir, "SampleLibrary", "TestModels");
          fullPath = Path.Combine(sampleLibDir, filename);
          if (File.Exists(fullPath))
            return Path.GetFullPath(fullPath);

          var parent = Path.GetDirectoryName(dir);
          if (parent == null || parent == dir) break;
          dir = parent;
        }
      }

      return Path.Combine(Directory.GetCurrentDirectory(), "TestModels", filename);
    }
  }

  /// <summary>
  /// Shared fixture that opens walls.rvt once for all Revit tests.
  /// Opens on the main thread and waits for Revit to finish processing.
  /// </summary>
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

  /// <summary>
  /// Defines the collection so all Revit test classes run sequentially
  /// and share the WallsDocFixture (document opened once).
  /// </summary>
  [CollectionDefinition("Revit")]
  public class RevitCollection : ICollectionFixture<WallsDocFixture> { }

  // ---------------------------------------------------------------------------
  // Test classes — all in [Collection("Revit")] so they share the fixture
  // and run sequentially (no parallel document opens).
  // ---------------------------------------------------------------------------

  [Collection("Revit")]
  public class RevitDocumentTests
  {
    private readonly WallsDocFixture _fixture;
    public RevitDocumentTests(WallsDocFixture fixture) { _fixture = fixture; }

    [Fact]
    public void ApplicationIsInitialized()
    {
      Assert.NotNull(xru.App);
    }

    [Fact]
    public void CanOpenDocument()
    {
      Assert.NotNull(_fixture.Doc);
      Assert.False(_fixture.Doc.IsFamilyDocument);
      Assert.NotNull(_fixture.Doc.Title);
    }

    [Fact]
    public void DocumentHasActiveView()
    {
      if (xru.IsHeadless) return; // ActiveView requires UI

      var activeView = _fixture.Doc.ActiveView;
      Assert.NotNull(activeView);
      Assert.NotEqual(ViewType.Undefined, activeView.ViewType);
    }
  }

  [Collection("Revit")]
  public class ElementCollectorTests
  {
    private readonly Document _doc;
    public ElementCollectorTests(WallsDocFixture fixture) { _doc = fixture.Doc; }

    [Fact]
    public void CollectAllWalls()
    {
      IList<Element> walls = null;
      xru.DispatchToMainThread(() =>
      {
        walls = new FilteredElementCollector(_doc)
          .WhereElementIsNotElementType()
          .OfCategory(BuiltInCategory.OST_Walls)
          .ToElements();
      });

      Assert.NotEmpty(walls);
      Assert.All(walls, w => Assert.IsAssignableFrom<Wall>(w));
    }

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
        Assert.True(volumeParam.AsDouble() > 0, $"Wall {wall.Id} has zero or negative volume");
      }
    }

    [Fact]
    public void CollectWallTypes()
    {
      List<WallType> wallTypes = null;
      xru.DispatchToMainThread(() =>
      {
        wallTypes = new FilteredElementCollector(_doc)
          .WhereElementIsElementType()
          .OfCategory(BuiltInCategory.OST_Walls)
          .Cast<WallType>()
          .ToList();
      });

      Assert.NotEmpty(wallTypes);
      Assert.All(wallTypes, wt => Assert.NotNull(wt.Name));
    }

    [Fact]
    public void CollectLevels()
    {
      List<Level> levels = null;
      xru.DispatchToMainThread(() =>
      {
        levels = new FilteredElementCollector(_doc)
          .OfClass(typeof(Level))
          .Cast<Level>()
          .OrderBy(l => l.Elevation)
          .ToList();
      });

      Assert.NotEmpty(levels);
      for (int i = 1; i < levels.Count; i++)
      {
        Assert.True(levels[i].Elevation >= levels[i - 1].Elevation,
          $"Level {levels[i].Name} elevation ({levels[i].Elevation}) should be >= {levels[i - 1].Name} ({levels[i - 1].Elevation})");
      }
    }

    [Fact]
    public void CollectViewsInDocument()
    {
      List<View> views = null;
      xru.DispatchToMainThread(() =>
      {
        views = new FilteredElementCollector(_doc)
          .OfClass(typeof(View))
          .Cast<View>()
          .Where(v => !v.IsTemplate)
          .ToList();
      });

      Assert.NotEmpty(views);
    }
  }

  [Collection("Revit")]
  public class TransactionTests
  {
    private readonly Document _doc;
    public TransactionTests(WallsDocFixture fixture) { _doc = fixture.Doc; }

    [Fact]
    public void CreateAndDeleteWall()
    {
      int wallCountBefore = 0;
      xru.DispatchToMainThread(() =>
      {
        wallCountBefore = new FilteredElementCollector(_doc)
          .WhereElementIsNotElementType()
          .OfCategory(BuiltInCategory.OST_Walls)
          .GetElementCount();
      });

      xru.Run(() =>
      {
        using (Transaction t = new Transaction(_doc, "Test - Create Wall"))
        {
          t.Start();

          var level = new FilteredElementCollector(_doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .First();

          var start = new XYZ(0, 0, 0);
          var end = new XYZ(20, 0, 0);
          var line = Line.CreateBound(start, end);

          var wall = Wall.Create(_doc, line, level.Id, false);
          Assert.NotNull(wall);
          Assert.True(wall.Id != ElementId.InvalidElementId);

          t.RollBack();
        }
      }, _doc).Wait();

      int wallCountAfter = 0;
      xru.DispatchToMainThread(() =>
      {
        wallCountAfter = new FilteredElementCollector(_doc)
          .WhereElementIsNotElementType()
          .OfCategory(BuiltInCategory.OST_Walls)
          .GetElementCount();
      });

      Assert.Equal(wallCountBefore, wallCountAfter);
    }

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

      Assert.NotNull(wall);

      xru.Run(() =>
      {
        using (Transaction t = new Transaction(_doc, "Test - Modify Offset"))
        {
          t.Start();
          offsetParam.Set(originalOffset + 5.0);
          Assert.Equal(originalOffset + 5.0, offsetParam.AsDouble(), precision: 5);
          t.RollBack();
        }
      }, _doc).Wait();

      double finalOffset = 0;
      xru.DispatchToMainThread(() =>
      {
        finalOffset = offsetParam.AsDouble();
      });
      Assert.Equal(originalOffset, finalOffset, precision: 5);
    }

    [Fact]
    public void RunInTransactionHelper()
    {
      IList<Element> walls = null;
      xru.DispatchToMainThread(() =>
      {
        walls = new FilteredElementCollector(_doc)
          .WhereElementIsNotElementType()
          .OfCategory(BuiltInCategory.OST_Walls)
          .ToElements();
      });

      xru.RunInTransaction(() =>
      {
        foreach (var wall in walls)
        {
          var param = wall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET);
          var offset = UnitUtils.ConvertToInternalUnits(500, param.GetUnitTypeId());
          param.Set(offset);
        }
      }, _doc, "Set Wall Offsets").Wait();

      foreach (var wall in walls)
      {
        double val = 0;
        xru.DispatchToMainThread(() =>
        {
          val = wall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET).AsDouble();
        });
        Assert.True(val > 0, $"Wall {wall.Id} offset should be > 0 after transaction");
      }
    }
  }

  [Collection("Revit")]
  public class GeometryExtractionTests
  {
    private readonly Document _doc;
    public GeometryExtractionTests(WallsDocFixture fixture) { _doc = fixture.Doc; }

    [Fact]
    public void WallHasSolid()
    {
      List<Solid> solids = null;
      xru.DispatchToMainThread(() =>
      {
        var wall = new FilteredElementCollector(_doc)
          .WhereElementIsNotElementType()
          .OfCategory(BuiltInCategory.OST_Walls)
          .FirstElement();

        Assert.NotNull(wall);

        var options = new Options { ComputeReferences = true };
        var geomElement = wall.get_Geometry(options);
        Assert.NotNull(geomElement);

        solids = geomElement
          .OfType<Solid>()
          .Where(s => s.Volume > 0)
          .ToList();
      });

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
      Wall wall = null;
      xru.DispatchToMainThread(() =>
      {
        wall = new FilteredElementCollector(_doc)
          .WhereElementIsNotElementType()
          .OfCategory(BuiltInCategory.OST_Walls)
          .FirstElement() as Wall;
      });

      Assert.NotNull(wall);

      LocationCurve location = null;
      Line line = null;
      xru.DispatchToMainThread(() =>
      {
        location = wall.Location as LocationCurve;
        line = location?.Curve as Line;
      });

      Assert.NotNull(location);
      Assert.IsType<Line>(location.Curve);
      Assert.True(line.Length > 0, "Wall length should be positive");
    }

    [Fact]
    public void WallBoundingBoxIsValid()
    {
      BoundingBoxXYZ bb = null;
      xru.DispatchToMainThread(() =>
      {
        var wall = new FilteredElementCollector(_doc)
          .WhereElementIsNotElementType()
          .OfCategory(BuiltInCategory.OST_Walls)
          .FirstElement();

        Assert.NotNull(wall);
        bb = wall.get_BoundingBox(null);
      });

      Assert.NotNull(bb);
      Assert.True(bb.Max.X >= bb.Min.X, "BoundingBox Max.X should be >= Min.X");
      Assert.True(bb.Max.Y >= bb.Min.Y, "BoundingBox Max.Y should be >= Min.Y");
      Assert.True(bb.Max.Z >= bb.Min.Z, "BoundingBox Max.Z should be >= Min.Z");
    }
  }

  /// <summary>
  /// Unit conversion tests — no document needed, thread-safe.
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
      double sqFeet = 10.0;
      double sqMeters = UnitUtils.ConvertFromInternalUnits(sqFeet, UnitTypeId.SquareMeters);
      Assert.True(sqMeters > 0);
      Assert.True(sqMeters < sqFeet);
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
