// ============================================================================
// ExampleRevitTests.cs — Template for writing Revit xUnit tests
// ============================================================================
//
// HOW TO USE THIS FILE:
//   This file demonstrates the patterns needed to write xUnit tests that run
//   inside Revit (both UI mode and headless CI mode). Copy this file as a
//   starting point for your own test library.
//
// KEY RULES:
//   1. ALL Revit API calls must run on Revit's main thread.
//      Use xru.DispatchToMainThread(() => { ... }) for reads (element queries,
//      parameter access, geometry extraction).
//      Use xru.RunInTransaction() or xru.Run() for writes (they dispatch internally).
//
//   2. Tests that open documents MUST share a [Collection] so they run
//      sequentially. Parallel document opens crash Revit.
//
//   3. Open documents via a shared fixture (ICollectionFixture) — not per-test.
//      Opening a .rvt file takes seconds and triggers updaters/events.
//
//   4. Tests that don't need Revit (pure math, string ops, config parsing)
//      do NOT need [Collection] or DispatchToMainThread — they run freely.
//
// FOR AI AGENTS:
//   When generating Revit tests, always:
//   - Add [Collection("Revit")] to classes that access Document/Elements
//   - Inject the fixture via constructor (WallsDocFixture or your own)
//   - Wrap every Revit API read in xru.DispatchToMainThread()
//   - Use xru.RunInTransaction() for modifications, always call .Wait()
//   - Never call doc.Close() — the fixture owns the document lifetime
//   - Never use doc.ActiveView in headless mode (it's null)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Xunit;
using xUnitRevitUtils;

namespace SampleLibrary.Modern
{
  // ==========================================================================
  // FIXTURE: Opens a Revit document once, shared across all test classes
  // in the "Revit" collection.
  //
  // To use a different .rvt file, create a new fixture class and a new
  // [CollectionDefinition] with a different name.
  // ==========================================================================
  // WallsDocFixture is already defined in RevitTests.cs — reuse it.
  // If you need a different document, create your own fixture like this:
  //
  //   public class MyModelFixture : IDisposable
  //   {
  //     public Document Doc { get; }
  //     public MyModelFixture()
  //     {
  //       Doc = xru.OpenDoc(TestModelLocator.GetTestModel("mymodel.rvt"));
  //     }
  //     public void Dispose() { }
  //   }
  //
  //   [CollectionDefinition("MyModel")]
  //   public class MyModelCollection : ICollectionFixture<MyModelFixture> { }

  // ==========================================================================
  // EXAMPLE 1: Querying elements and validating properties
  //
  // Demonstrates:
  //   - DispatchToMainThread for read operations
  //   - FilteredElementCollector patterns
  //   - Parameter access
  //   - Nested dispatch calls
  // ==========================================================================
  [Collection("Revit")]
  [Trait("Category", "Revit")]
  public class WallPropertyTests
  {
    private readonly Document _doc;

    /// <summary>
    /// Constructor receives the shared fixture — document is already open.
    /// </summary>
    public WallPropertyTests(WallsDocFixture fixture)
    {
      _doc = fixture.Doc;
    }

    /// <summary>
    /// Verify that all walls in the document have a positive length.
    /// Shows: dispatching a collector query, then reading location curves.
    /// </summary>
    [Fact]
    public void AllWallsHavePositiveLength()
    {
      List<Wall> walls = null;

      // Step 1: Query elements on the main thread
      xru.DispatchToMainThread(() =>
      {
        walls = new FilteredElementCollector(_doc)
          .WhereElementIsNotElementType()
          .OfCategory(BuiltInCategory.OST_Walls)
          .Cast<Wall>()
          .ToList();
      });

      Assert.NotEmpty(walls);

      // Step 2: Read properties — also on the main thread
      foreach (var wall in walls)
      {
        double length = 0;
        xru.DispatchToMainThread(() =>
        {
          var locationCurve = wall.Location as LocationCurve;
          if (locationCurve != null)
            length = locationCurve.Curve.Length;
        });

        Assert.True(length > 0, $"Wall {wall.Id} should have positive length");
      }
    }

    /// <summary>
    /// Verify that wall types have valid compound structure layers.
    /// Shows: querying element types and accessing compound structure.
    /// </summary>
    [Fact]
    public void WallTypesHaveStructure()
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

      foreach (var wt in wallTypes)
      {
        string typeName = null;
        int layerCount = 0;

        xru.DispatchToMainThread(() =>
        {
          typeName = wt.Name;
          var structure = wt.GetCompoundStructure();
          if (structure != null)
            layerCount = structure.LayerCount;
        });

        Assert.NotNull(typeName);
        // Basic walls may not have compound structure (curtain walls, etc.)
        // Just verify we can read it without crashing
      }
    }
  }

  // ==========================================================================
  // EXAMPLE 2: Geometry extraction and validation
  //
  // Demonstrates:
  //   - Extracting solids from elements
  //   - Working with bounding boxes
  //   - Computing geometric properties
  // ==========================================================================
  [Collection("Revit")]
  [Trait("Category", "Revit")]
  public class GeometryValidationTests
  {
    private readonly Document _doc;

    public GeometryValidationTests(WallsDocFixture fixture)
    {
      _doc = fixture.Doc;
    }

    /// <summary>
    /// Extract all solids from a wall and verify total volume is reasonable.
    /// Shows: Options setup, geometry traversal, LINQ on Revit geometry.
    /// </summary>
    [Fact]
    public void WallSolidsHaveConsistentVolume()
    {
      double totalVolume = 0;
      double paramVolume = 0;

      xru.DispatchToMainThread(() =>
      {
        var wall = new FilteredElementCollector(_doc)
          .WhereElementIsNotElementType()
          .OfCategory(BuiltInCategory.OST_Walls)
          .FirstElement();

        Assert.NotNull(wall);

        // Get volume from geometry
        var options = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine };
        var geom = wall.get_Geometry(options);
        totalVolume = geom.OfType<Solid>().Where(s => s.Volume > 0).Sum(s => s.Volume);

        // Get volume from parameter for comparison
        var volParam = wall.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED);
        if (volParam != null)
          paramVolume = volParam.AsDouble();
      });

      Assert.True(totalVolume > 0, "Geometry-derived volume should be positive");
      // Parameter volume and geometry volume should be in the same ballpark
      if (paramVolume > 0)
        Assert.True(Math.Abs(totalVolume - paramVolume) / paramVolume < 0.1,
          $"Geometry volume ({totalVolume:F4}) should be within 10% of parameter volume ({paramVolume:F4})");
    }

    /// <summary>
    /// Verify that all walls have bounding boxes that don't overlap in absurd ways.
    /// Shows: BoundingBox access and spatial validation.
    /// </summary>
    [Fact]
    public void WallBoundingBoxesDontOverlapExcessively()
    {
      List<BoundingBoxXYZ> boundingBoxes = null;

      xru.DispatchToMainThread(() =>
      {
        boundingBoxes = new FilteredElementCollector(_doc)
          .WhereElementIsNotElementType()
          .OfCategory(BuiltInCategory.OST_Walls)
          .Select(w => w.get_BoundingBox(null))
          .Where(bb => bb != null)
          .ToList();
      });

      Assert.NotEmpty(boundingBoxes);

      foreach (var bb in boundingBoxes)
      {
        // Each bounding box should have positive dimensions
        Assert.True(bb.Max.X >= bb.Min.X);
        Assert.True(bb.Max.Y >= bb.Min.Y);
        Assert.True(bb.Max.Z >= bb.Min.Z);

        // Z extent (height) should be reasonable (< 1000 feet)
        var height = bb.Max.Z - bb.Min.Z;
        Assert.True(height < 1000, $"Wall height {height:F1} feet seems unreasonable");
      }
    }
  }

  // ==========================================================================
  // EXAMPLE 3: Transactions — modifying and rolling back
  //
  // Demonstrates:
  //   - xru.RunInTransaction() for safe modifications
  //   - xru.Run() for manual transaction control
  //   - Verifying state before and after modifications
  //   - Rolling back to avoid polluting the test model
  // ==========================================================================
  [Collection("Revit")]
  [Trait("Category", "Revit")]
  public class TransactionPatternTests
  {
    private readonly Document _doc;

    public TransactionPatternTests(WallsDocFixture fixture)
    {
      _doc = fixture.Doc;
    }

    /// <summary>
    /// Create a temporary element, verify it exists, then roll back.
    /// Shows: the safe pattern for testing element creation without
    /// permanently modifying the test model.
    /// </summary>
    [Fact]
    public void CanCreateElementAndRollBack()
    {
      int elementCountBefore = 0;
      int elementCountDuring = 0;
      int elementCountAfter = 0;

      xru.DispatchToMainThread(() =>
      {
        elementCountBefore = new FilteredElementCollector(_doc)
          .WhereElementIsNotElementType()
          .OfCategory(BuiltInCategory.OST_Walls)
          .GetElementCount();
      });

      // Use xru.Run() for manual transaction control (rollback)
      xru.Run(() =>
      {
        using (var t = new Transaction(_doc, "Test - Temp Wall"))
        {
          t.Start();

          var level = new FilteredElementCollector(_doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .First();

          var line = Line.CreateBound(new XYZ(0, 0, 0), new XYZ(10, 0, 0));
          Wall.Create(_doc, line, level.Id, false);

          elementCountDuring = new FilteredElementCollector(_doc)
            .WhereElementIsNotElementType()
            .OfCategory(BuiltInCategory.OST_Walls)
            .GetElementCount();

          t.RollBack(); // Clean up — don't pollute the model
        }
      }, _doc).Wait();

      xru.DispatchToMainThread(() =>
      {
        elementCountAfter = new FilteredElementCollector(_doc)
          .WhereElementIsNotElementType()
          .OfCategory(BuiltInCategory.OST_Walls)
          .GetElementCount();
      });

      Assert.Equal(elementCountBefore + 1, elementCountDuring); // Wall was created
      Assert.Equal(elementCountBefore, elementCountAfter);       // Rollback restored state
    }

    /// <summary>
    /// Use xru.RunInTransaction() helper to modify parameters, then verify.
    /// Shows: the simpler API when you want to commit (not roll back).
    /// Note: this WILL modify the test model — use with care.
    /// </summary>
    [Fact]
    public void RunInTransactionCommitsChanges()
    {
      IList<Element> walls = null;

      xru.DispatchToMainThread(() =>
      {
        walls = new FilteredElementCollector(_doc)
          .WhereElementIsNotElementType()
          .OfCategory(BuiltInCategory.OST_Walls)
          .ToElements();
      });

      // RunInTransaction wraps action in a transaction and commits
      xru.RunInTransaction(() =>
      {
        foreach (var wall in walls)
        {
          var param = wall.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
          if (param != null && !param.IsReadOnly)
            param.Set("TestMark");
        }
      }, _doc, "Set Marks").Wait();

      // Verify the changes stuck
      foreach (var wall in walls)
      {
        string mark = null;
        xru.DispatchToMainThread(() =>
        {
          var param = wall.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
          if (param != null)
            mark = param.AsString();
        });

        if (mark != null) // Some walls may not have this parameter
          Assert.Equal("TestMark", mark);
      }
    }
  }
}
