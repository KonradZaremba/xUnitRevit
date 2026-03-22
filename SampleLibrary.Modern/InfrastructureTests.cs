using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Xunit;
using xUnitRevit;

namespace SampleLibrary.Modern
{
  /// <summary>
  /// Tests for the xUnitRevit infrastructure (TestResultWriter, Configuration, TestResult).
  /// These run without Revit and validate the headless runner pipeline.
  /// </summary>
  public class TestResultWriterTests : IDisposable
  {
    private readonly string _tempDir;

    public TestResultWriterTests()
    {
      _tempDir = Path.Combine(Path.GetTempPath(), "xUnitRevitTests_" + Guid.NewGuid().ToString("N")[..8]);
      Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
      try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void WritesValidJUnitXml()
    {
      var results = new List<TestResult>
      {
        new TestResult { TestName = "Test1", ClassName = "MyClass", Outcome = TestOutcome.Passed, ExecutionTime = 0.5 }
      };
      var path = Path.Combine(_tempDir, "results.xml");

      TestResultWriter.WriteJUnitXml(results, path, TimeSpan.FromSeconds(1));

      Assert.True(File.Exists(path));
      var doc = new XmlDocument();
      doc.Load(path);
      Assert.NotNull(doc.DocumentElement);
      Assert.Equal("testsuites", doc.DocumentElement.Name);
    }

    [Fact]
    public void CorrectlyCountsPassFailSkip()
    {
      var results = new List<TestResult>
      {
        new TestResult { TestName = "Pass1", ClassName = "Suite", Outcome = TestOutcome.Passed, ExecutionTime = 0.1 },
        new TestResult { TestName = "Pass2", ClassName = "Suite", Outcome = TestOutcome.Passed, ExecutionTime = 0.2 },
        new TestResult { TestName = "Fail1", ClassName = "Suite", Outcome = TestOutcome.Failed, ExecutionTime = 0.3, ErrorMessage = "boom" },
        new TestResult { TestName = "Skip1", ClassName = "Suite", Outcome = TestOutcome.Skipped, ErrorMessage = "not ready" },
      };
      var path = Path.Combine(_tempDir, "counts.xml");

      TestResultWriter.WriteJUnitXml(results, path, TimeSpan.FromSeconds(2));

      var doc = new XmlDocument();
      doc.Load(path);
      var root = doc.DocumentElement;
      Assert.Equal("4", root.GetAttribute("tests"));
      Assert.Equal("1", root.GetAttribute("failures"));
      Assert.Equal("1", root.GetAttribute("skipped"));
    }

    [Fact]
    public void GroupsResultsByClassName()
    {
      var results = new List<TestResult>
      {
        new TestResult { TestName = "A1", ClassName = "ClassA", Outcome = TestOutcome.Passed },
        new TestResult { TestName = "A2", ClassName = "ClassA", Outcome = TestOutcome.Passed },
        new TestResult { TestName = "B1", ClassName = "ClassB", Outcome = TestOutcome.Failed, ErrorMessage = "err" },
      };
      var path = Path.Combine(_tempDir, "grouped.xml");

      TestResultWriter.WriteJUnitXml(results, path, TimeSpan.FromSeconds(1));

      var doc = new XmlDocument();
      doc.Load(path);
      var suites = doc.DocumentElement.SelectNodes("testsuite");
      Assert.Equal(2, suites.Count);
    }

    [Fact]
    public void FailureElementContainsMessageAndStackTrace()
    {
      var results = new List<TestResult>
      {
        new TestResult
        {
          TestName = "FailTest",
          ClassName = "Suite",
          Outcome = TestOutcome.Failed,
          ErrorMessage = "Expected 42 but got 54",
          ErrorStackTrace = "at MyTest.Run() line 10"
        }
      };
      var path = Path.Combine(_tempDir, "failure.xml");

      TestResultWriter.WriteJUnitXml(results, path, TimeSpan.FromSeconds(1));

      var doc = new XmlDocument();
      doc.Load(path);
      var failure = doc.DocumentElement.SelectSingleNode("//failure");
      Assert.NotNull(failure);
      Assert.Equal("Expected 42 but got 54", failure.Attributes["message"].Value);
      Assert.Contains("at MyTest.Run()", failure.InnerText);
    }

    [Fact]
    public void SkippedElementContainsReason()
    {
      var results = new List<TestResult>
      {
        new TestResult
        {
          TestName = "SkipTest",
          ClassName = "Suite",
          Outcome = TestOutcome.Skipped,
          ErrorMessage = "Not implemented yet"
        }
      };
      var path = Path.Combine(_tempDir, "skipped.xml");

      TestResultWriter.WriteJUnitXml(results, path, TimeSpan.FromSeconds(1));

      var doc = new XmlDocument();
      doc.Load(path);
      var skipped = doc.DocumentElement.SelectSingleNode("//skipped");
      Assert.NotNull(skipped);
      Assert.Equal("Not implemented yet", skipped.Attributes["message"].Value);
    }

    [Fact]
    public void CreatesOutputDirectoryIfMissing()
    {
      var nestedDir = Path.Combine(_tempDir, "sub", "deep");
      var path = Path.Combine(nestedDir, "results.xml");
      var results = new List<TestResult>
      {
        new TestResult { TestName = "T", ClassName = "C", Outcome = TestOutcome.Passed }
      };

      TestResultWriter.WriteJUnitXml(results, path, TimeSpan.FromSeconds(0.1));

      Assert.True(File.Exists(path));
    }

    [Fact]
    public void HandlesEmptyResultList()
    {
      var path = Path.Combine(_tempDir, "empty.xml");

      TestResultWriter.WriteJUnitXml(new List<TestResult>(), path, TimeSpan.Zero);

      var doc = new XmlDocument();
      doc.Load(path);
      Assert.Equal("0", doc.DocumentElement.GetAttribute("tests"));
    }

    [Fact]
    public void NullClassNameDefaultsToUnknown()
    {
      var results = new List<TestResult>
      {
        new TestResult { TestName = "Orphan", ClassName = null, Outcome = TestOutcome.Passed }
      };
      var path = Path.Combine(_tempDir, "null_class.xml");

      TestResultWriter.WriteJUnitXml(results, path, TimeSpan.FromSeconds(1));

      var doc = new XmlDocument();
      doc.Load(path);
      var suite = doc.DocumentElement.SelectSingleNode("testsuite");
      Assert.Equal("Unknown", suite.Attributes["name"].Value);
    }
  }

  public class ConfigurationTests
  {
    [Fact]
    public void DefaultValuesAreCorrect()
    {
      var config = new Configuration();

      Assert.Empty(config.startupAssemblies);
      Assert.False(config.autoStart);
      Assert.False(config.headless);
      Assert.Equal("junit", config.resultFormat);
      Assert.Equal("./TestResults.xml", config.resultPath);
    }

    [Fact]
    public void CanDeserializeFromJson()
    {
      var json = @"{
        ""startupAssemblies"": [""C:\\test\\MyTests.dll"", ""C:\\test\\Other.dll""],
        ""autoStart"": true,
        ""headless"": true,
        ""resultFormat"": ""junit"",
        ""resultPath"": ""C:\\output\\Results.xml""
      }";

      var config = System.Text.Json.JsonSerializer.Deserialize<Configuration>(json,
        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

      Assert.Equal(2, config.startupAssemblies.Count);
      Assert.True(config.autoStart);
      Assert.True(config.headless);
      Assert.Equal("C:\\output\\Results.xml", config.resultPath);
    }

    [Fact]
    public void PartialJsonUsesDefaults()
    {
      var json = @"{ ""headless"": true }";

      var config = System.Text.Json.JsonSerializer.Deserialize<Configuration>(json,
        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

      Assert.True(config.headless);
      Assert.False(config.autoStart);
      Assert.Equal("junit", config.resultFormat);
    }
  }

  public class TestResultModelTests
  {
    [Fact]
    public void DefaultOutcomeIsPassed()
    {
      var result = new TestResult();
      Assert.Equal(TestOutcome.Passed, result.Outcome);
    }

    [Fact]
    public void AllPropertiesRoundTrip()
    {
      var result = new TestResult
      {
        TestName = "MyTest",
        ClassName = "MyClass",
        AssemblyName = "MyAssembly",
        Outcome = TestOutcome.Failed,
        ExecutionTime = 1.234,
        ErrorMessage = "assertion failed",
        ErrorStackTrace = "at line 42"
      };

      Assert.Equal("MyTest", result.TestName);
      Assert.Equal("MyClass", result.ClassName);
      Assert.Equal("MyAssembly", result.AssemblyName);
      Assert.Equal(TestOutcome.Failed, result.Outcome);
      Assert.Equal(1.234, result.ExecutionTime);
      Assert.Equal("assertion failed", result.ErrorMessage);
      Assert.Equal("at line 42", result.ErrorStackTrace);
    }

    [Theory]
    [InlineData(TestOutcome.Passed)]
    [InlineData(TestOutcome.Failed)]
    [InlineData(TestOutcome.Skipped)]
    public void AllOutcomesAreValid(TestOutcome outcome)
    {
      var result = new TestResult { Outcome = outcome };
      Assert.Equal(outcome, result.Outcome);
    }
  }
}
