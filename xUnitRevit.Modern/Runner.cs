using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using xUnitRevitUtils;

namespace xUnitRevit
{
  /// <summary>
  /// Responsible for launching the xUnit WPF interface and initializing xru with Revit data.
  /// .NET 8 version using System.Text.Json and a minimal custom WPF runner.
  /// </summary>
  public static class Runner
  {
    internal static Configuration Config = new Configuration();

    internal static void Launch(UIApplication uiapp)
    {
      try
      {
        var queue = new List<Action>();
        var eventHandler = ExternalEvent.Create(new ExternalEventHandler(queue));

        xru.Initialize(uiapp, SynchronizationContext.Current, eventHandler, queue);

        var main = new TestRunnerWindow();
        main.Title = "xUnit Revit by Speckle";
        main.MaxHeight = 800;

        // Pre-load assemblies from config
        if (Config.startupAssemblies != null && Config.startupAssemblies.Count > 0)
        {
          main.SetStartupAssemblies(Config.startupAssemblies);
        }

        main.Show();
      }
      catch (Exception e)
      {
        // Log to debug output rather than silently swallowing
        System.Diagnostics.Debug.WriteLine($"xUnitRevit Runner.Launch failed: {e}");
      }
    }

    internal static void ReadConfig()
    {
      try
      {
        var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        var path = Path.Combine(dir, "config.json");
        var json = File.ReadAllText(path);
        Config = JsonSerializer.Deserialize<Configuration>(json, new JsonSerializerOptions
        {
          PropertyNameCaseInsensitive = true
        });
      }
      catch
      { }
    }
  }
}
