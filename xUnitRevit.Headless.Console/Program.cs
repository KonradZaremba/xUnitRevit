using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Xunit;
using Xunit.Abstractions;
using xUnitRevit;

/// <summary>
/// Standalone console app to test the headless runner pipeline.
/// Discovers and runs xUnit tests in specified assemblies, outputs JUnit XML results.
/// This simulates what HeadlessRunner does inside Revit, minus the Revit dependency.
/// </summary>
class Program
{
  static int Main(string[] args)
  {
    Console.WriteLine("=== xUnitRevit Headless Test Runner (Console) ===");
    Console.WriteLine();

    var includeRevit = false;
    var positional = new List<string>();
    foreach (var arg in args)
    {
      if (arg == "--include-revit")
        includeRevit = true;
      else if (arg.StartsWith("--"))
      {
        Console.Error.WriteLine($"ERROR: Unknown option: {arg}");
        return 2;
      }
      else
        positional.Add(arg);
    }

    if (positional.Count == 0)
    {
      Console.WriteLine("Usage: xUnitRevit.Headless.Console <test-assembly.dll> [result-path.xml] [--include-revit]");
      Console.WriteLine();
      Console.WriteLine("Options:");
      Console.WriteLine("  --include-revit   Also run tests tagged [Trait(\"Category\", \"Revit\")]");
      Console.WriteLine("                    (skipped by default — they need the Revit runtime)");
      Console.WriteLine();
      Console.WriteLine("Exit codes: 0 = all passed, 1 = test failures, 2 = infrastructure failure");
      Console.WriteLine();
      Console.WriteLine("Example:");
      Console.WriteLine("  xUnitRevit.Headless.Console SampleLibrary.Modern.dll TestResults.xml");
      return 1;
    }

    var assemblyPath = Path.GetFullPath(positional[0]);
    var resultPath = positional.Count > 1 ? positional[1] : "TestResults.xml";

    if (!File.Exists(assemblyPath))
    {
      Console.Error.WriteLine($"ERROR: Assembly not found: {assemblyPath}");
      return 1;
    }

    // Register assembly resolver for test DLL's directory
    var assemblyDir = Path.GetDirectoryName(assemblyPath)!;
    AssemblyLoadContext.Default.Resolving += (ctx, name) =>
    {
      var candidate = Path.Combine(assemblyDir, name.Name + ".dll");
      if (File.Exists(candidate))
        return ctx.LoadFromAssemblyPath(candidate);
      return null;
    };

    Console.WriteLine($"Assembly: {assemblyPath}");
    Console.WriteLine($"Results:  {Path.GetFullPath(resultPath)}");
    Console.WriteLine();

    var allResults = new List<TestResult>();
    var stopwatch = Stopwatch.StartNew();

    try
    {
      using var controller = new XunitFrontController(
        AppDomainSupport.Denied,
        assemblyPath,
        diagnosticMessageSink: new ConsoleDiagnosticSink());

      // Discover
      Console.WriteLine("Discovering tests...");
      var discoveryVisitor = new ConsoleDiscoveryVisitor();
      controller.Find(false, discoveryVisitor, TestFrameworkOptions.ForDiscovery());
      discoveryVisitor.Finished.WaitOne();
      Console.WriteLine($"Found {discoveryVisitor.TestCases.Count} tests.");
      Console.WriteLine();

      // Filter out Revit-only tests unless --include-revit was passed
      const string SkipReason = "Requires Revit runtime — excluded by default (pass --include-revit to run)";
      var toRun = new List<ITestCase>();
      foreach (var tc in discoveryVisitor.TestCases)
      {
        var isRevit = tc.Traits != null
          && tc.Traits.TryGetValue("Category", out var vals)
          && vals.Any(v => string.Equals(v, "Revit", StringComparison.OrdinalIgnoreCase));

        if (!includeRevit && isRevit)
        {
          allResults.Add(new TestResult
          {
            TestName = tc.DisplayName,
            ClassName = tc.TestMethod?.TestClass?.Class?.Name,
            AssemblyName = tc.TestMethod?.TestClass?.TestCollection?.TestAssembly?.Assembly?.Name,
            Outcome = TestOutcome.Skipped,
            ErrorMessage = SkipReason
          });
          continue;
        }
        toRun.Add(tc);
      }

      // Execute
      Console.WriteLine($"Running {toRun.Count} of {discoveryVisitor.TestCases.Count} tests ({allResults.Count} excluded as Revit-only)...");
      Console.WriteLine(new string('-', 70));

      if (toRun.Count > 0)
      {
        var executionVisitor = new ConsoleExecutionVisitor(allResults);
        controller.RunTests(toRun, executionVisitor, TestFrameworkOptions.ForExecution());
        executionVisitor.Finished.WaitOne();
      }
    }
    catch (Exception ex)
    {
      Console.Error.WriteLine($"ERROR: {ex.Message}");
      allResults.Add(new TestResult
      {
        TestName = $"Assembly: {Path.GetFileName(assemblyPath)}",
        AssemblyName = assemblyPath,
        Outcome = TestOutcome.Failed,
        ErrorMessage = ex.Message,
        ErrorStackTrace = ex.StackTrace
      });
    }

    stopwatch.Stop();

    // Write JUnit XML
    var infraFailure = false;
    try
    {
      TestResultWriter.WriteJUnitXml(allResults, resultPath, stopwatch.Elapsed);
    }
    catch (Exception ex)
    {
      Console.Error.WriteLine($"ERROR: Could not write results to '{Path.GetFullPath(resultPath)}': {ex.Message}");
      infraFailure = true;
    }

    // Summary
    Console.WriteLine(new string('-', 70));
    var passed = allResults.Count(r => r.Outcome == TestOutcome.Passed);
    var failed = allResults.Count(r => r.Outcome == TestOutcome.Failed);
    var skipped = allResults.Count(r => r.Outcome == TestOutcome.Skipped);

    Console.ForegroundColor = failed > 0 ? ConsoleColor.Red : ConsoleColor.Green;
    Console.WriteLine();
    Console.WriteLine($"Results: {passed} passed, {failed} failed, {skipped} skipped ({stopwatch.Elapsed.TotalSeconds:F2}s)");
    Console.ResetColor();
    if (!infraFailure)
      Console.WriteLine($"JUnit XML written to: {Path.GetFullPath(resultPath)}");

    if (infraFailure) return 2; // infrastructure failure — results not persisted
    return failed > 0 ? 1 : 0;
  }
}

class ConsoleDiscoveryVisitor : Xunit.Sdk.LongLivedMarshalByRefObject, IMessageSink
{
  public List<ITestCase> TestCases { get; } = new();
  public System.Threading.ManualResetEvent Finished { get; } = new(false);

  public bool OnMessage(IMessageSinkMessage message)
  {
    if (message is ITestCaseDiscoveryMessage discovery)
      TestCases.Add(discovery.TestCase);
    if (message is IDiscoveryCompleteMessage)
      Finished.Set();
    return true;
  }
}

class ConsoleExecutionVisitor : Xunit.Sdk.LongLivedMarshalByRefObject, IMessageSink
{
  private readonly List<TestResult> _results;
  public System.Threading.ManualResetEvent Finished { get; } = new(false);

  public ConsoleExecutionVisitor(List<TestResult> results) => _results = results;

  public bool OnMessage(IMessageSinkMessage message)
  {
    if (message is ITestPassed passed)
    {
      Console.ForegroundColor = ConsoleColor.Green;
      Console.Write("  PASS ");
      Console.ResetColor();
      Console.WriteLine($"{passed.TestCase.DisplayName} ({passed.ExecutionTime:F3}s)");
      _results.Add(new TestResult
      {
        TestName = passed.TestCase.DisplayName,
        ClassName = passed.TestCase.TestMethod?.TestClass?.Class?.Name,
        AssemblyName = passed.TestCase.TestMethod?.TestClass?.TestCollection?.TestAssembly?.Assembly?.Name,
        Outcome = TestOutcome.Passed,
        ExecutionTime = (double)passed.ExecutionTime
      });
    }
    else if (message is ITestFailed failed)
    {
      Console.ForegroundColor = ConsoleColor.Red;
      Console.Write("  FAIL ");
      Console.ResetColor();
      Console.WriteLine($"{failed.TestCase.DisplayName} ({failed.ExecutionTime:F3}s)");
      Console.ForegroundColor = ConsoleColor.DarkRed;
      Console.WriteLine($"       {string.Join("; ", failed.Messages)}");
      Console.ResetColor();
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
    }
    else if (message is ITestSkipped skipped)
    {
      Console.ForegroundColor = ConsoleColor.Yellow;
      Console.Write("  SKIP ");
      Console.ResetColor();
      Console.WriteLine($"{skipped.TestCase.DisplayName} - {skipped.Reason}");
      _results.Add(new TestResult
      {
        TestName = skipped.TestCase.DisplayName,
        ClassName = skipped.TestCase.TestMethod?.TestClass?.Class?.Name,
        AssemblyName = skipped.TestCase.TestMethod?.TestClass?.TestCollection?.TestAssembly?.Assembly?.Name,
        Outcome = TestOutcome.Skipped,
        ErrorMessage = skipped.Reason
      });
    }
    else if (message is ITestAssemblyFinished)
    {
      Finished.Set();
    }
    return true;
  }
}

class ConsoleDiagnosticSink : Xunit.Sdk.LongLivedMarshalByRefObject, IMessageSink
{
  public bool OnMessage(IMessageSinkMessage message) => true;
}
