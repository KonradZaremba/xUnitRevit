using System;
using System.IO;
using System.Linq;
using Xunit;

namespace SampleLibrary.Modern
{
  /// <summary>
  /// Pure-IO tests for TestModelLocator. No Revit dependency — runs in the console runner.
  /// </summary>
  public class TestModelLocatorTests : IDisposable
  {
    private readonly string _root;

    public TestModelLocatorTests()
    {
      _root = Path.Combine(Path.GetTempPath(), "xur-loc-" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
      try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string CreateModel(string relativeDir, string filename)
    {
      var dir = Path.Combine(_root, relativeDir);
      Directory.CreateDirectory(dir);
      var path = Path.Combine(dir, filename);
      File.WriteAllText(path, "fake rvt content");
      return path;
    }

    [Fact]
    public void FindsFileInTestModelsSubdirOfBaseDir()
    {
      var expected = CreateModel("TestModels", "a.rvt");

      var result = TestModelLocator.GetTestModel("a.rvt", new[] { _root });

      Assert.Equal(Path.GetFullPath(expected), result);
      Assert.True(Path.IsPathRooted(result));
    }

    [Fact]
    public void FindsFileInSampleLibraryTestModelsSubdir()
    {
      var expected = CreateModel(Path.Combine("SampleLibrary", "TestModels"), "b.rvt");

      var result = TestModelLocator.GetTestModel("b.rvt", new[] { _root });

      Assert.Equal(Path.GetFullPath(expected), result);
    }

    [Fact]
    public void WalksUpToParentDirectories()
    {
      var expected = CreateModel("TestModels", "c.rvt");
      var deepBase = Path.Combine(_root, "a", "b", "c");
      Directory.CreateDirectory(deepBase);

      var result = TestModelLocator.GetTestModel("c.rvt", new[] { deepBase });

      Assert.Equal(Path.GetFullPath(expected), result);
    }

    [Fact]
    public void DoesNotWalkBeyondMaxDepth()
    {
      CreateModel("TestModels", "d.rvt");
      // Base dir 7 levels below _root — the 6-level walk stops one short
      var deepBase = Path.Combine(_root, "1", "2", "3", "4", "5", "6", "7");
      Directory.CreateDirectory(deepBase);

      Assert.Throws<FileNotFoundException>(
        () => TestModelLocator.GetTestModel("d.rvt", new[] { deepBase }));
    }

    [Fact]
    public void ThrowsFileNotFoundListingProbedPaths()
    {
      var ex = Assert.Throws<FileNotFoundException>(
        () => TestModelLocator.GetTestModel("missing.rvt", new[] { _root }));

      Assert.Contains("missing.rvt", ex.Message);
      Assert.Contains(Path.Combine(_root, "TestModels", "missing.rvt"), ex.Message);
    }

    [Fact]
    public void SkipsNullAndEmptyBaseDirs()
    {
      var expected = CreateModel("TestModels", "e.rvt");

      var result = TestModelLocator.GetTestModel("e.rvt", new string?[] { null, "", _root });

      Assert.Equal(Path.GetFullPath(expected), result);
    }

    [Fact]
    public void VersionedCandidatesPrefersVersionSpecificFirst()
    {
      var candidates = TestModelLocator.VersionedCandidates("walls.rvt", "2026").ToArray();

      Assert.Equal(new[] { "walls_2026.rvt", "walls.rvt" }, candidates);
    }

    [Fact]
    public void VersionedCandidatesWithoutVersionReturnsBaseOnly()
    {
      var candidates = TestModelLocator.VersionedCandidates("walls.rvt", null).ToArray();

      Assert.Equal(new[] { "walls.rvt" }, candidates);
    }

    [Fact]
    public void FirstBaseDirWins()
    {
      var first = CreateModel(Path.Combine("first", "TestModels"), "f.rvt");
      CreateModel(Path.Combine("second", "TestModels"), "f.rvt");

      var result = TestModelLocator.GetTestModel("f.rvt", new[]
      {
        Path.Combine(_root, "first"),
        Path.Combine(_root, "second"),
      });

      Assert.Equal(Path.GetFullPath(first), result);
    }
  }
}
