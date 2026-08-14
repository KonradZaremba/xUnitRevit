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
        App.Log("Runner.Launch: creating ExternalEvent...");
        var queue = new List<Action>();
        var eventHandler = ExternalEvent.Create(new ExternalEventHandler(queue));

        App.Log("Runner.Launch: initializing xru...");
        xru.Initialize(uiapp, SynchronizationContext.Current, eventHandler, queue);

        App.Log("Runner.Launch: creating TestRunnerWindow...");
        var main = new TestRunnerWindow();
        main.Title = "xUnit Revit by Speckle";
        main.MaxHeight = 800;

        if (Config.startupAssemblies != null && Config.startupAssemblies.Count > 0)
        {
          App.Log($"Runner.Launch: loading {Config.startupAssemblies.Count} startup assemblies...");
          main.SetStartupAssemblies(Config.startupAssemblies);
          App.Log("Runner.Launch: assemblies loaded");
        }

        App.Log("Runner.Launch: showing window...");
        main.Show();

        if (Config.startupAssemblies != null && Config.startupAssemblies.Count > 0)
        {
          main.StartWatching(Config.startupAssemblies);
          App.Log("Runner.Launch: file watcher started");
        }

        App.Log("Runner.Launch: done");
      }
      catch (Exception e)
      {
        App.Log($"Runner.Launch ERROR: {e}");
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
