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

    if (args.Length == 0)
    {
      Console.WriteLine("Usage: xUnitRevit.Headless.Console <test-assembly.dll> [result-path.xml]");
      Console.WriteLine();
      Console.WriteLine("Example:");
      Console.WriteLine("  xUnitRevit.Headless.Console SampleLibrary.Modern.dll TestResults.xml");
      return 1;
    }

    var assemblyPath = Path.GetFullPath(args[0]);
    var resultPath = args.Length > 1 ? args[1] : "TestResults.xml";

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

      // Execute
      Console.WriteLine("Running tests...");
      Console.WriteLine(new string('-', 70));

      var executionVisitor = new ConsoleExecutionVisitor(allResults);
      controller.RunTests(discoveryVisitor.TestCases, executionVisitor, TestFrameworkOptions.ForExecution());
      executionVisitor.Finished.WaitOne();
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
    TestResultWriter.WriteJUnitXml(allResults, resultPath, stopwatch.Elapsed);

    // Summary
    Console.WriteLine(new string('-', 70));
    var passed = allResults.Count(r => r.Outcome == TestOutcome.Passed);
    var failed = allResults.Count(r => r.Outcome == TestOutcome.Failed);
    var skipped = allResults.Count(r => r.Outcome == TestOutcome.Skipped);

    Console.ForegroundColor = failed > 0 ? ConsoleColor.Red : ConsoleColor.Green;
    Console.WriteLine();
    Console.WriteLine($"Results: {passed} passed, {failed} failed, {skipped} skipped ({stopwatch.Elapsed.TotalSeconds:F2}s)");
    Console.ResetColor();
    Console.WriteLine($"JUnit XML written to: {Path.GetFullPath(resultPath)}");

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
