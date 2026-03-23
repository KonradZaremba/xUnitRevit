using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Autodesk.Revit.ApplicationServices;
using Xunit;
using Xunit.Abstractions;
using xUnitRevitUtils;

namespace xUnitRevit
{
  /// <summary>
  /// Headless test runner that executes xUnit tests without any UI.
  /// Outputs results to file (JUnit XML format) for CI/CD consumption.
  /// </summary>
  public static class HeadlessRunner
  {
    private static bool _resolverRegistered;
    private static string _logPath;
    private static volatile bool _testsComplete;
    private static bool _exitAfterTests;
    private static string _capturedResultPath;

    /// <summary>
    /// Called by Revit's Idling event on the main thread.
    /// Processes Revit API work items dispatched from test background threads.
    /// </summary>
    public static void OnIdling(object sender, Autodesk.Revit.UI.Events.IdlingEventArgs e)
    {
      // Process all pending work items on the main thread
      while (xru.HeadlessWorkQueue.TryTake(out var item))
      {
        var (work, done, error) = item;
        try
        {
          work();
        }
        catch (Exception ex)
        {
          error[0] = ex;
        }
        finally
        {
          done.Set();
        }
      }

      if (_testsComplete)
      {
        // Unregister idling handler
        if (sender is Autodesk.Revit.UI.UIApplication uiapp)
          uiapp.Idling -= OnIdling;

        if (_exitAfterTests)
        {
          Log("exitAfterTests=true - signaling completion. External runner will close Revit.");
          try
          {
            var sentinel = Path.ChangeExtension(_capturedResultPath, ".done");
            File.WriteAllText(sentinel, DateTime.Now.ToString("o"));
          }
          catch { }
        }
      }
      else
      {
        // Request another Idling callback soon
        e.SetRaiseWithoutDelay();
      }
    }

    internal static void Log(string message)
    {
      var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
      Debug.WriteLine(line);
      try
      {
        if (_logPath != null)
          File.AppendAllText(_logPath, line + Environment.NewLine);
      }
      catch { }
    }

    public static void Launch(Application app, Configuration config)
    {
      // Set up log file next to results
      var resultPath = config.resultPath ?? "./TestResults.xml";
      if (!Path.IsPathRooted(resultPath))
      {
        var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        resultPath = Path.Combine(dir, resultPath);
      }
      _logPath = Path.ChangeExtension(resultPath, ".log");

      // Clear previous log
      try { File.WriteAllText(_logPath, ""); } catch { }

      Log("=== xUnitRevit Headless Runner ===");
      Log($"Revit version: {app.VersionNumber}");
      Log($"Results path: {resultPath}");
      Log($"Assemblies to test: {config.startupAssemblies.Count}");

      // Initialize xru in headless mode
      xru.InitializeHeadless(app);
      Log("xru initialized in headless mode");

      // Tests run on a background thread (xUnit needs its own threads).
      // Revit API document tests skip in headless mode (no UI thread dispatch available).
      // Unit conversion tests work because UnitUtils is thread-safe.
      var capturedResultPath = resultPath;
      var exitAfterTests = config.exitAfterTests;

      System.Threading.Tasks.Task.Run(() =>
      {
        try
        {
          RunAllTests(config, capturedResultPath);
        }
        catch (Exception ex)
        {
          Log($"FATAL ERROR: {ex}");
          try
          {
            WriteResults(new List<TestResult>
            {
              new TestResult
              {
                TestName = "FATAL",
                ClassName = "HeadlessRunner",
                Outcome = TestOutcome.Failed,
                ErrorMessage = ex.Message,
                ErrorStackTrace = ex.StackTrace
              }
            }, capturedResultPath, TimeSpan.Zero);
          }
          catch { }
        }
        finally
        {
          if (exitAfterTests)
          {
            Log("exitAfterTests=true - signaling completion.");
            try
            {
              var sentinel = Path.ChangeExtension(capturedResultPath, ".done");
              File.WriteAllText(sentinel, DateTime.Now.ToString("o"));
            }
            catch { }
          }
        }
      });
    }

    private static void RunAllTests(Configuration config, string resultPath)
    {
      var allResults = new List<TestResult>();
      var stopwatch = Stopwatch.StartNew();

      foreach (var assemblyPath in config.startupAssemblies)
      {
        if (!File.Exists(assemblyPath))
        {
          Log($"WARNING: Assembly not found: {assemblyPath}");
          continue;
        }

        // Register assembly resolver for each test DLL's directory
        RegisterAssemblyResolver(Path.GetDirectoryName(assemblyPath));

        Log($"Running tests in: {assemblyPath}");
        try
        {
          var results = RunAssembly(assemblyPath);
          allResults.AddRange(results);
        }
        catch (Exception ex)
        {
          Log($"ERROR running assembly {assemblyPath}: {ex.Message}");
          allResults.Add(new TestResult
          {
            TestName = $"Assembly: {Path.GetFileName(assemblyPath)}",
            AssemblyName = assemblyPath,
            Outcome = TestOutcome.Failed,
            ErrorMessage = ex.Message,
            ErrorStackTrace = ex.StackTrace
          });
        }
      }

      stopwatch.Stop();
      WriteResults(allResults, resultPath, stopwatch.Elapsed);
    }

    private static void WriteResults(List<TestResult> allResults, string resultPath, TimeSpan elapsed)
    {
      try
      {
        // Write JUnit XML results
        TestResultWriter.WriteJUnitXml(allResults, resultPath, elapsed);
      }
      catch (Exception ex)
      {
        Log($"ERROR writing XML results: {ex.Message}");
      }

      // Summary
      var passed = allResults.Count(r => r.Outcome == TestOutcome.Passed);
      var failed = allResults.Count(r => r.Outcome == TestOutcome.Failed);
      var skipped = allResults.Count(r => r.Outcome == TestOutcome.Skipped);

      Log($"");
      Log($"=== RESULTS: {passed} passed, {failed} failed, {skipped} skipped ({elapsed.TotalSeconds:F2}s) ===");
      Log($"JUnit XML: {resultPath}");
      Log($"Log: {_logPath}");

      if (failed > 0)
        Environment.ExitCode = 1;
    }

    private static readonly HashSet<string> _resolvedDirs = new();

    private static void RegisterAssemblyResolver(string directory)
    {
      if (_resolverRegistered && _resolvedDirs.Contains(directory))
        return;

      _resolvedDirs.Add(directory);

      if (!_resolverRegistered)
      {
        AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
          foreach (var dir in _resolvedDirs)
          {
            var candidate = Path.Combine(dir, name.Name + ".dll");
            if (File.Exists(candidate))
              return ctx.LoadFromAssemblyPath(candidate);
          }
          return null;
        };
        _resolverRegistered = true;
      }
    }

    private static List<TestResult> RunAssembly(string assemblyPath)
    {
      var results = new List<TestResult>();

      try
      {
        using (var controller = new XunitFrontController(
          AppDomainSupport.Denied,
          assemblyPath,
          diagnosticMessageSink: new HeadlessMessageSink()))
        {
          // Discover tests
          var discoveryVisitor = new HeadlessDiscoveryVisitor();
          controller.Find(false, discoveryVisitor, TestFrameworkOptions.ForDiscovery());
          if (!discoveryVisitor.Finished.WaitOne(TimeSpan.FromSeconds(60)))
          {
            Log($"TIMEOUT: Test discovery timed out after 60s for {Path.GetFileName(assemblyPath)}");
            return results;
          }

          Log($"Discovered {discoveryVisitor.TestCases.Count} tests in {Path.GetFileName(assemblyPath)}");

          // Execute tests
          var executionVisitor = new HeadlessExecutionVisitor(results);
          controller.RunTests(discoveryVisitor.TestCases, executionVisitor, TestFrameworkOptions.ForExecution());
          if (!executionVisitor.Finished.WaitOne(TimeSpan.FromMinutes(5)))
          {
            Log($"TIMEOUT: Test execution timed out after 5min for {Path.GetFileName(assemblyPath)}");
          }
        }
      }
      catch (Exception ex)
      {
        Log($"ERROR running tests in {assemblyPath}: {ex}");
        results.Add(new TestResult
        {
          TestName = $"Assembly: {Path.GetFileName(assemblyPath)}",
          AssemblyName = assemblyPath,
          Outcome = TestOutcome.Failed,
          ErrorMessage = ex.Message,
          ErrorStackTrace = ex.StackTrace
        });
      }

      return results;
    }
  }

  public enum TestOutcome
  {
    Passed,
    Failed,
    Skipped
  }

  public class TestResult
  {
    public string TestName { get; set; }
    public string ClassName { get; set; }
    public string AssemblyName { get; set; }
    public TestOutcome Outcome { get; set; }
    public double ExecutionTime { get; set; }
    public string ErrorMessage { get; set; }
    public string ErrorStackTrace { get; set; }
  }

  internal class HeadlessDiscoveryVisitor : Xunit.Sdk.LongLivedMarshalByRefObject, IMessageSink
  {
    public List<ITestCase> TestCases { get; } = new List<ITestCase>();
    public System.Threading.ManualResetEvent Finished { get; } = new System.Threading.ManualResetEvent(false);

    public bool OnMessage(IMessageSinkMessage message)
    {
      if (message is ITestCaseDiscoveryMessage discovery)
      {
        TestCases.Add(discovery.TestCase);
      }

      if (message is IDiscoveryCompleteMessage)
      {
        Finished.Set();
      }

      return true;
    }
  }

  internal class HeadlessExecutionVisitor : Xunit.Sdk.LongLivedMarshalByRefObject, IMessageSink
  {
    private readonly List<TestResult> _results;
    public System.Threading.ManualResetEvent Finished { get; } = new System.Threading.ManualResetEvent(false);

    public HeadlessExecutionVisitor(List<TestResult> results)
    {
      _results = results;
    }

    public bool OnMessage(IMessageSinkMessage message)
    {
      if (message is ITestPassed passed)
      {
        _results.Add(new TestResult
        {
          TestName = passed.TestCase.DisplayName,
          ClassName = passed.TestCase.TestMethod?.TestClass?.Class?.Name,
          AssemblyName = passed.TestCase.TestMethod?.TestClass?.TestCollection?.TestAssembly?.Assembly?.Name,
          Outcome = TestOutcome.Passed,
          ExecutionTime = (double)passed.ExecutionTime
        });
        HeadlessRunner.Log($"  PASS: {passed.TestCase.DisplayName} ({passed.ExecutionTime:F3}s)");
      }
      else if (message is ITestFailed failed)
      {
        _results.Add(new TestResult
        {
          TestName = failed.TestCase.DisplayName,
          ClassName = failed.TestCase.TestMethod?.TestClass?.Class?.Name,
          AssemblyName = failed.TestCase.TestMethod?.TestClass?.TestCollection?.TestAssembly?.Assembly?.Name,
          Outcome = TestOutcome.Failed,
          ExecutionTime = (double)failed.ExecutionTime,
          ErrorMessage = string.Join(Environment.NewLine, failed.Messages),
          ErrorStackTrace = string.Join(Environment.NewLine, failed.StackTraces)
        });
        HeadlessRunner.Log($"  FAIL: {failed.TestCase.DisplayName} - {string.Join("; ", failed.Messages)}");
      }
      else if (message is ITestSkipped skipped)
      {
        _results.Add(new TestResult
        {
          TestName = skipped.TestCase.DisplayName,
          ClassName = skipped.TestCase.TestMethod?.TestClass?.Class?.Name,
          AssemblyName = skipped.TestCase.TestMethod?.TestClass?.TestCollection?.TestAssembly?.Assembly?.Name,
          Outcome = TestOutcome.Skipped,
          ErrorMessage = skipped.Reason
        });
        HeadlessRunner.Log($"  SKIP: {skipped.TestCase.DisplayName} - {skipped.Reason}");
      }
      else if (message is ITestAssemblyFinished)
      {
        Finished.Set();
      }

      return true;
    }
  }

  internal class HeadlessMessageSink : Xunit.Sdk.LongLivedMarshalByRefObject, IMessageSink
  {
    public bool OnMessage(IMessageSinkMessage message) => true;
  }
}
