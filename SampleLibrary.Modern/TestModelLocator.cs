using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using xUnitRevitUtils;

namespace SampleLibrary.Modern
{
  /// <summary>
  /// Resolves test model paths across execution contexts (Revit headless, Revit UI, console runner).
  ///
  /// Prefers a version-specific model (e.g. walls_2026.rvt) matching the running Revit version so
  /// each version opens a NATIVE-format file with no on-open upgrade/conversion, falling back to the
  /// shared cross-version model (walls.rvt) when no version-specific file exists.
  /// </summary>
  internal static class TestModelLocator
  {
    internal const int MaxParentDepth = 6;

    /// <summary>
    /// Finds a test model, preferring a version-specific variant for the running Revit version.
    /// Probes TestModels folders relative to the test assembly location and the CWD.
    /// </summary>
    internal static string GetTestModel(string filename)
    {
      var baseDirs = new[]
      {
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
        Directory.GetCurrentDirectory(),
      };

      var probed = new List<string>();
      foreach (var candidate in VersionedCandidates(filename, GetRevitVersion()))
      {
        var hit = TryFind(candidate, baseDirs, probed);
        if (hit != null) return hit;
      }

      throw NotFound(filename, probed);
    }

    /// <summary>
    /// Testable core for a single filename: probes each base dir and up to
    /// <see cref="MaxParentDepth"/> parents. Throws <see cref="FileNotFoundException"/>
    /// listing all probed paths on miss.
    /// </summary>
    internal static string GetTestModel(string filename, IEnumerable<string?> baseDirs)
    {
      var probed = new List<string>();
      var hit = TryFind(filename, baseDirs, probed);
      if (hit != null) return hit;

      throw NotFound(filename, probed);
    }

    private static FileNotFoundException NotFound(string filename, List<string> probed) =>
      new FileNotFoundException(
        $"Test model '{filename}' not found. Probed paths:{Environment.NewLine}" +
        string.Join(Environment.NewLine, probed));

    /// <summary>
    /// Builds candidate filenames in priority order. With a known version:
    /// [ "walls_2026.rvt", "walls.rvt" ]. Without: [ "walls.rvt" ].
    /// </summary>
    internal static IEnumerable<string> VersionedCandidates(string filename, string version)
    {
      if (!string.IsNullOrEmpty(version))
      {
        var name = Path.GetFileNameWithoutExtension(filename);
        var ext = Path.GetExtension(filename);
        yield return $"{name}_{version}{ext}";
      }
      yield return filename;
    }

    /// <summary>Returns the full path if found, else null (appending probed paths).</summary>
    private static string TryFind(string filename, IEnumerable<string?> baseDirs, List<string> probed)
    {
      foreach (var baseDir in baseDirs)
      {
        if (string.IsNullOrEmpty(baseDir)) continue;

        var dir = baseDir;
        for (int i = 0; i < MaxParentDepth; i++)
        {
          var candidates = new[]
          {
            Path.Combine(dir, "TestModels", filename),
            Path.Combine(dir, "SampleLibrary", "TestModels", filename),
          };

          foreach (var candidate in candidates)
          {
            if (File.Exists(candidate))
              return Path.GetFullPath(candidate);
            probed.Add(candidate);
          }

          var parent = Path.GetDirectoryName(dir);
          if (parent == null || parent == dir) break;
          dir = parent;
        }
      }

      return null;
    }

    /// <summary>Running Revit version (e.g. "2026"), or null outside Revit (console runner).</summary>
    private static string GetRevitVersion()
    {
      try { return xru.App?.VersionNumber; }
      catch { return null; }
    }
  }
}
