using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;

namespace xUnitRevit
{
  /// <summary>
  /// Writes test results in JUnit XML format for CI/CD pipeline consumption.
  /// Compatible with GitHub Actions, Azure Pipelines, Jenkins, etc.
  /// </summary>
  public static class TestResultWriter
  {
    public static void WriteJUnitXml(List<TestResult> results, string outputPath, TimeSpan totalTime)
    {
      var dir = Path.GetDirectoryName(outputPath);
      if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
      {
        Directory.CreateDirectory(dir);
      }

      var settings = new XmlWriterSettings
      {
        Indent = true,
        IndentChars = "  "
      };

      using (var writer = XmlWriter.Create(outputPath, settings))
      {
        writer.WriteStartDocument();
        writer.WriteStartElement("testsuites");

        // Group results by assembly/class
        var suites = results.GroupBy(r => r.ClassName ?? "Unknown");

        var totalTests = results.Count;
        var totalFailures = results.Count(r => r.Outcome == TestOutcome.Failed);
        var totalSkipped = results.Count(r => r.Outcome == TestOutcome.Skipped);
        var totalTimeSeconds = totalTime.TotalSeconds;

        writer.WriteAttributeString("tests", totalTests.ToString());
        writer.WriteAttributeString("failures", totalFailures.ToString());
        writer.WriteAttributeString("skipped", totalSkipped.ToString());
        writer.WriteAttributeString("time", totalTimeSeconds.ToString("F3", CultureInfo.InvariantCulture));

        foreach (var suite in suites)
        {
          writer.WriteStartElement("testsuite");
          writer.WriteAttributeString("name", suite.Key);
          writer.WriteAttributeString("tests", suite.Count().ToString());
          writer.WriteAttributeString("failures", suite.Count(r => r.Outcome == TestOutcome.Failed).ToString());
          writer.WriteAttributeString("skipped", suite.Count(r => r.Outcome == TestOutcome.Skipped).ToString());
          writer.WriteAttributeString("time", suite.Sum(r => r.ExecutionTime).ToString("F3", CultureInfo.InvariantCulture));
          writer.WriteAttributeString("timestamp", DateTime.UtcNow.ToString("o"));

          foreach (var result in suite)
          {
            writer.WriteStartElement("testcase");
            writer.WriteAttributeString("name", result.TestName);
            writer.WriteAttributeString("classname", result.ClassName ?? "Unknown");
            writer.WriteAttributeString("time", result.ExecutionTime.ToString("F3", CultureInfo.InvariantCulture));

            if (result.Outcome == TestOutcome.Failed)
            {
              writer.WriteStartElement("failure");
              writer.WriteAttributeString("message", result.ErrorMessage ?? "");
              if (!string.IsNullOrEmpty(result.ErrorStackTrace))
              {
                writer.WriteString(result.ErrorStackTrace);
              }
              writer.WriteEndElement(); // failure
            }
            else if (result.Outcome == TestOutcome.Skipped)
            {
              writer.WriteStartElement("skipped");
              if (!string.IsNullOrEmpty(result.ErrorMessage))
              {
                writer.WriteAttributeString("message", result.ErrorMessage);
              }
              writer.WriteEndElement(); // skipped
            }

            writer.WriteEndElement(); // testcase
          }

          writer.WriteEndElement(); // testsuite
        }

        writer.WriteEndElement(); // testsuites
        writer.WriteEndDocument();
      }
    }
  }
}
