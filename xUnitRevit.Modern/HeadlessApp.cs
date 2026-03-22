using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;

namespace xUnitRevit
{
  /// <summary>
  /// DB-only application for headless Revit (Revit Platform Services).
  /// Does not require UIApplication — runs tests without any UI.
  /// Register this in the .addin manifest as Type="DBApplication".
  /// </summary>
  class HeadlessApp : IExternalDBApplication
  {
    public ExternalDBApplicationResult OnStartup(ControlledApplication app)
    {
      try
      {
        var config = ReadConfig();

        if (config.headless && config.startupAssemblies.Count > 0)
        {
          HeadlessRunner.Launch(app.GetType()
            .GetProperty("Application", BindingFlags.NonPublic | BindingFlags.Instance)?
            .GetValue(app) as Application ?? throw new InvalidOperationException("Cannot access Application from ControlledApplication"),
            config);
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"HeadlessApp startup failed: {ex}");
      }

      return ExternalDBApplicationResult.Succeeded;
    }

    public ExternalDBApplicationResult OnShutdown(ControlledApplication app)
    {
      return ExternalDBApplicationResult.Succeeded;
    }

    private Configuration ReadConfig()
    {
      try
      {
        var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        var path = Path.Combine(dir, "config.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<Configuration>(json, new JsonSerializerOptions
        {
          PropertyNameCaseInsensitive = true
        });
      }
      catch
      {
        return new Configuration();
      }
    }
  }
}
