using System.Collections.Generic;

namespace xUnitRevit
{
  /// <summary>
  /// Configuration file for xUnit Revit runner.
  /// Read from config.json alongside the add-in DLL.
  /// </summary>
  public class Configuration
  {
    public List<string> startupAssemblies { get; set; } = new List<string>();
    public bool autoStart { get; set; } = false;

    /// <summary>
    /// When true, runs tests without UI (for CI/CD and Revit Platform Services).
    /// </summary>
    public bool headless { get; set; } = false;

    /// <summary>
    /// Output format for test results: "junit" or "trx".
    /// </summary>
    public string resultFormat { get; set; } = "junit";

    /// <summary>
    /// Path where test results will be written.
    /// </summary>
    public string resultPath { get; set; } = "./TestResults.xml";
  }
}
