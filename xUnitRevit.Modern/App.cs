using System;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace xUnitRevit
{
  /// <summary>
  /// Revit External Application for .NET 8+ (Revit 2025+).
  /// Supports both UI and headless modes.
  /// </summary>
  class App : IExternalApplication
  {
    public Result OnStartup(UIControlledApplication a)
    {
      a.ControlledApplication.ApplicationInitialized += ControlledApplication_ApplicationInitialized;
      return Result.Succeeded;
    }

    private void ControlledApplication_ApplicationInitialized(object sender, Autodesk.Revit.DB.Events.ApplicationInitializedEventArgs e)
    {
      try
      {
        Runner.ReadConfig();
        Application app = sender as Application;

        if (Runner.Config.headless)
        {
          // No UIApplication needed in headless mode — avoids deadlock in Revit 2026
          HeadlessRunner.Launch(app, Runner.Config);
          return;
        }

        UIApplication uiapp = new UIApplication(app);
        if (Runner.Config.autoStart)
        {
          Runner.Launch(uiapp);
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"xUnitRevit startup error: {ex}");
      }
    }

    public Result OnShutdown(UIControlledApplication a)
    {
      return Result.Succeeded;
    }
  }
}
