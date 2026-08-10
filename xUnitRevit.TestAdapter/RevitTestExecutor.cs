using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

namespace xUnitRevit.TestAdapter
{
  /// <summary>
  /// Custom vstest adapter that runs xUnit tests inside Revit (headless mode).
  ///
  /// When "dotnet test" or VS Code Test Explorer runs tests, this adapter:
  ///   1. Deploys the test DLL to Revit's Addins folder
  ///   2. Writes config.json for headless execution
  ///   3. Launches Revit.exe
  ///   4. Waits for JUnit XML results
  ///   5. Parses results back into vstest TestResult objects
  ///
  /// This means "dotnet test" just works — no special commands needed.
  /// </summary>
  [ExtensionUri(ExecutorUri)]
  public class RevitTestExecutor : ITestExecutor
  {
    public const string ExecutorUri = "executor://xunitrevit";

    private Process? _revitProcess;

    public void RunTests(IEnumerable<TestCase>? tests, IRunContext? runContext, IFrameworkHandle? frameworkHandle)
    {
      if (tests == null || frameworkHandle == null) return;

      var testList = tests.ToList();
      if (testList.Count == 0) return;

      // Get the test assembly path from the first test
      var assemblyPath = testList[0].Source;
      if (string.IsNullOrEmpty(assemblyPath)) return;

      frameworkHandle.SendMessage(TestMessageLevel.Informational, $"xUnitRevit: Running {testList.Count} tests in Revit...");

      var resultsPath = RunInRevit(assemblyPath, frameworkHandle);

      if (resultsPath != null && File.Exists(resultsPath))
      {
        MapResults(testList, resultsPath, frameworkHandle);
      }
      else
      {
        // No results — mark all as failed
        foreach (var test in testList)
        {
          var result = new TestResult(test)
          {
            Outcome = TestOutcome.Failed,
            ErrorMessage = "Revit did not produce test results. Check if Revit is installed and licensed."
          };
          frameworkHandle.RecordResult(result);
        }
      }
    }

    public void RunTests(IEnumerable<string>? sources, IRunContext? runContext, IFrameworkHandle? frameworkHandle)
    {
      // Not used — discovery provides TestCase objects
    }

    public void Cancel()
    {
      try { _revitProcess?.Kill(); } catch { }
    }

    private string? RunInRevit(string assemblyPath, IFrameworkHandle handle)
    {
      var assemblyDir = Path.GetDirectoryName(assemblyPath)!;
      var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);

      // Detect Revit version from env or default
      var revitVersion = Environment.GetEnvironmentVariable("REVIT_VERSION") ?? "2026";
      var timeout = int.Parse(Environment.GetEnvironmentVariable("REVIT_TEST_TIMEOUT") ?? "600");
      var addinsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Autodesk", "REVIT", "Addins", revitVersion, "xUnitRevit"
      );
      var revitExe = $@"C:\Program Files\Autodesk\Revit {revitVersion}\Revit.exe";
      var resultsPath = Path.Combine(assemblyDir, "RevitTestResults.xml");

      if (!File.Exists(revitExe))
      {
        handle.SendMessage(TestMessageLevel.Error, $"xUnitRevit: Revit not found at {revitExe}");
        return null;
      }

      // Deploy
      Directory.CreateDirectory(addinsDir);
      foreach (var file in Directory.GetFiles(assemblyDir, "*.*", SearchOption.AllDirectories))
      {
        var rel = Path.GetRelativePath(assemblyDir, file);
        var dest = Path.Combine(addinsDir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(file, dest, true);
      }

      handle.SendMessage(TestMessageLevel.Informational, $"xUnitRevit: Deployed to {addinsDir}");

      // Write config
      var config = $@"{{
  ""startupAssemblies"": [""{assemblyPath.Replace("\\", "\\\\")}""],
  ""autoStart"": true,
  ""headless"": true,
  ""resultFormat"": ""junit"",
  ""resultPath"": ""{resultsPath.Replace("\\", "\\\\")}"",
  ""exitAfterTests"": true
}}";
      File.WriteAllText(Path.Combine(addinsDir, "config.json"), config);

      // Clean previous results
      if (File.Exists(resultsPath)) File.Delete(resultsPath);

      // Copy addin manifest
      var addinManifest = Path.Combine(addinsDir, "..", "xUnitRevit.addin");
      if (!File.Exists(addinManifest))
      {
        // Search for it in the deployed files
        var candidate = Directory.GetFiles(addinsDir, "*.addin", SearchOption.AllDirectories).FirstOrDefault();
        if (candidate != null)
          File.Copy(candidate, addinManifest, true);
      }

      // Kill existing Revit
      foreach (var proc in Process.GetProcessesByName("Revit"))
      {
        try { proc.Kill(); } catch { }
      }
      System.Threading.Thread.Sleep(2000);

      // Launch Revit
      handle.SendMessage(TestMessageLevel.Informational, $"xUnitRevit: Launching Revit {revitVersion}...");
      _revitProcess = Process.Start(new ProcessStartInfo(revitExe) { UseShellExecute = false });

      // Wait for results
      var sw = Stopwatch.StartNew();
      var sentinelPath = Path.ChangeExtension(resultsPath, ".done");

      while (sw.Elapsed.TotalSeconds < timeout)
      {
        System.Threading.Thread.Sleep(5000);

        if (_revitProcess?.HasExited == true)
        {
          if (File.Exists(resultsPath)) break;
          handle.SendMessage(TestMessageLevel.Warning, $"xUnitRevit: Revit exited with code {_revitProcess.ExitCode}");
          break;
        }

        if (File.Exists(sentinelPath))
        {
          handle.SendMessage(TestMessageLevel.Informational, $"xUnitRevit: Tests completed in {sw.Elapsed.TotalSeconds:F0}s");
          try { File.Delete(sentinelPath); } catch { }
          break;
        }

        if (File.Exists(resultsPath) && sw.Elapsed.TotalSeconds > 15)
        {
          System.Threading.Thread.Sleep(2000);
          break;
        }
      }

      // Close Revit
      if (_revitProcess != null && !_revitProcess.HasExited)
      {
        _revitProcess.CloseMainWindow();
        if (!_revitProcess.WaitForExit(10000))
        {
          try { _revitProcess.Kill(); } catch { }
        }
      }

      return File.Exists(resultsPath) ? resultsPath : null;
    }

    private void MapResults(List<TestCase> tests, string resultsPath, IFrameworkHandle frameworkHandle)
    {
      var doc = new XmlDocument();
      doc.Load(resultsPath);

      var testcases = doc.SelectNodes("//testcase");
      if (testcases == null) return;

      // Build lookup from JUnit XML
      var junitResults = new Dictionary<string, (string outcome, double time, string? message)>();
      foreach (XmlNode tc in testcases)
      {
        var name = tc.Attributes?["name"]?.Value ?? "";
        var time = double.TryParse(tc.Attributes?["time"]?.Value, out var t) ? t : 0;

        if (tc.SelectSingleNode("failure") is XmlNode failure)
        {
          var msg = failure.Attributes?["message"]?.Value ?? failure.InnerText;
          junitResults[name] = ("failed", time, msg);
        }
        else if (tc.SelectSingleNode("skipped") is XmlNode skipped)
        {
          var msg = skipped.Attributes?["message"]?.Value;
          junitResults[name] = ("skipped", time, msg);
        }
        else
        {
          junitResults[name] = ("passed", time, null);
        }
      }

      // Map back to vstest TestCase objects
      foreach (var test in tests)
      {
        var match = junitResults.FirstOrDefault(r =>
          r.Key.EndsWith($".{test.DisplayName}") ||
          r.Key.Contains(test.DisplayName) ||
          test.FullyQualifiedName.EndsWith(r.Key));

        var result = new TestResult(test);

        if (match.Key != null)
        {
          result.Duration = TimeSpan.FromSeconds(match.Value.time);
          result.Outcome = match.Value.outcome switch
          {
            "passed" => TestOutcome.Passed,
            "failed" => TestOutcome.Failed,
            "skipped" => TestOutcome.Skipped,
            _ => TestOutcome.None
          };
          if (match.Value.message != null)
            result.ErrorMessage = match.Value.message;
        }
        else
        {
          result.Outcome = TestOutcome.NotFound;
        }

        frameworkHandle.RecordResult(result);
      }

      var passed = junitResults.Count(r => r.Value.outcome == "passed");
      var failed = junitResults.Count(r => r.Value.outcome == "failed");
      frameworkHandle.SendMessage(TestMessageLevel.Informational,
        $"xUnitRevit: {passed} passed, {failed} failed, {junitResults.Count - passed - failed} skipped");
    }
  }
}
