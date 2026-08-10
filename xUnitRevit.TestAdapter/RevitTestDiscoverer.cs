using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using Xunit;
using Xunit.Abstractions;

namespace xUnitRevit.TestAdapter
{
  /// <summary>
  /// Discovers xUnit tests from compiled assemblies for the vstest platform.
  /// Used by "dotnet test" and VS Code Test Explorer to find test methods.
  ///
  /// This discoverer finds the same tests as xunit.runner.visualstudio but
  /// associates them with our RevitTestExecutor — so when run, they go
  /// through Revit instead of running locally.
  /// </summary>
  [DefaultExecutorUri(RevitTestExecutor.ExecutorUri)]
  [FileExtension(".dll")]
  public class RevitTestDiscoverer : ITestDiscoverer
  {
    public void DiscoverTests(
      IEnumerable<string> sources,
      IDiscoveryContext discoveryContext,
      IMessageLogger logger,
      ITestCaseDiscoverySink discoverySink)
    {
      foreach (var source in sources)
      {
        if (!File.Exists(source)) continue;

        try
        {
          logger.SendMessage(TestMessageLevel.Informational, $"xUnitRevit: discovering tests in {Path.GetFileName(source)}");

          using var controller = new XunitFrontController(
            AppDomainSupport.Denied,
            source,
            diagnosticMessageSink: new NullSink());

          var visitor = new DiscoveryVisitor(source, discoverySink);
          controller.Find(false, visitor, TestFrameworkOptions.ForDiscovery());
          visitor.Finished.WaitOne(TimeSpan.FromSeconds(30));

          logger.SendMessage(TestMessageLevel.Informational, $"xUnitRevit: found {visitor.Count} tests in {Path.GetFileName(source)}");
        }
        catch (Exception ex)
        {
          logger.SendMessage(TestMessageLevel.Warning, $"xUnitRevit: discovery failed for {source}: {ex.Message}");
        }
      }
    }
  }

  internal class DiscoveryVisitor : Xunit.Sdk.LongLivedMarshalByRefObject, IMessageSink
  {
    private readonly string _source;
    private readonly ITestCaseDiscoverySink _sink;

    public System.Threading.ManualResetEvent Finished { get; } = new(false);
    public int Count { get; private set; }

    public DiscoveryVisitor(string source, ITestCaseDiscoverySink sink)
    {
      _source = source;
      _sink = sink;
    }

    public bool OnMessage(IMessageSinkMessage message)
    {
      if (message is ITestCaseDiscoveryMessage discovery)
      {
        var tc = discovery.TestCase;
        var testCase = new TestCase(tc.DisplayName, new Uri(RevitTestExecutor.ExecutorUri), _source)
        {
          DisplayName = tc.DisplayName,
          FullyQualifiedName = $"{tc.TestMethod.TestClass.Class.Name}.{tc.TestMethod.Method.Name}",
          CodeFilePath = tc.SourceInformation?.FileName,
          LineNumber = tc.SourceInformation?.LineNumber ?? 0,
        };
        _sink.SendTestCase(testCase);
        Count++;
      }

      if (message is IDiscoveryCompleteMessage)
      {
        Finished.Set();
      }

      return true;
    }
  }

  internal class NullSink : Xunit.Sdk.LongLivedMarshalByRefObject, IMessageSink
  {
    public bool OnMessage(IMessageSinkMessage message) => true;
  }
}
