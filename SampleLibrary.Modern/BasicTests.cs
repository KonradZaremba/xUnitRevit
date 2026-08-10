using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SampleLibrary.Modern
{
  /// <summary>
  /// Basic tests that do NOT require Revit - used to verify headless runner pipeline.
  /// </summary>
  public class BasicTests
  {
    [Fact]
    public void SimpleAddition()
    {
      Assert.Equal(4, 2 + 2);
    }

    [Fact]
    public void StringContains()
    {
      Assert.Contains("Revit", "xUnitRevit Test Runner");
    }

    [Fact]
    public void ListHasExpectedCount()
    {
      var items = new List<string> { "Wall", "Floor", "Roof", "Door" };
      Assert.Equal(4, items.Count);
    }

    [Theory]
    [InlineData(304.8, 1.0)]
    [InlineData(609.6, 2.0)]
    [InlineData(914.4, 3.0)]
    public void MillimetersToFeetConversion(double mm, double expectedFeet)
    {
      // Simulating Revit unit conversion: 1 foot = 304.8 mm
      double feet = mm / 304.8;
      Assert.Equal(expectedFeet, feet, precision: 5);
    }

    [Fact]
    public void IntentionalFailure()
    {
      // Demonstrates failure reporting. Set XUNITREVIT_DEMO_FAILURES=1 to activate;
      // otherwise passes so default runs can be green.
      if (Environment.GetEnvironmentVariable("XUNITREVIT_DEMO_FAILURES") != "1")
        return;

      Assert.Equal(42, 6 * 9);
    }

    [Fact(Skip = "Skipped to verify skip reporting")]
    public void SkippedTest()
    {
      Assert.True(false, "Should never run");
    }
  }

  public class GeometryTests
  {
    [Fact]
    public void PointDistanceCalculation()
    {
      // Simulate XYZ distance calc like in Revit
      double x1 = 0, y1 = 0, z1 = 0;
      double x2 = 3, y2 = 4, z2 = 0;
      double distance = Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2) + Math.Pow(z2 - z1, 2));
      Assert.Equal(5.0, distance);
    }

    [Fact]
    public void AreaCalculation()
    {
      // Wall area: 10m x 3m = 30 sq meters
      double width = 10.0;
      double height = 3.0;
      double area = width * height;
      Assert.Equal(30.0, area);
    }

    [Fact]
    public void VolumeCalculation()
    {
      // Slab volume: 10m x 8m x 0.2m = 16 cubic meters
      double length = 10.0;
      double width = 8.0;
      double thickness = 0.2;
      double volume = length * width * thickness;
      Assert.Equal(16.0, volume, precision: 5);
    }
  }
}
