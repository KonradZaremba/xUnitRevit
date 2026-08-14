using System;
using System.IO;
using System.Reflection;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace xUnitRevit
{
  class App : IExternalApplication
  {
    private UIControlledApplication _uiCtrlApp;
    private bool _uiLaunched;
    private static string _logPath;

    public static void Log(string msg)
    {
      try
      {
        if (_logPath == null)
        {
          var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
          _logPath = Path.Combine(dir, "xUnitRevit_app.log");
        }
        File.AppendAllText(_logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
      }
      catch { }
    }

    public Result OnStartup(UIControlledApplication a)
    {
      try { File.WriteAllText(_logPath ?? Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "xUnitRevit_app.log"), ""); } catch { }
      Log("OnStartup called");

      // Global exception handlers to prevent Revit crashes from unhandled exceptions
      AppDomain.CurrentDomain.UnhandledException += (s, args) =>
      {
        Log($"UNHANDLED EXCEPTION: {args.ExceptionObject}");
      };
      System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, args) =>
      {
        Log($"UNOBSERVED TASK EXCEPTION: {args.Exception}");
        args.SetObserved(); // Prevent crash
      };

      try
      {
        _uiCtrlApp = a;
        Runner.ReadConfig();
        Log($"Config: headless={Runner.Config.headless}, autoStart={Runner.Config.autoStart}, assemblies={Runner.Config.startupAssemblies?.Count}");

        // Dialog/warning suppression is applied per-run (DialogSuppression.Begin/End), never globally —
        // leaving it on breaks other add-ins and eats the user's warnings. Just wire it up here.
        DialogSuppression.Configure(a);

        if (Runner.Config.headless)
        {
          Log("Registering ApplicationInitialized for headless mode");
          a.ControlledApplication.ApplicationInitialized += OnApplicationInitialized_Headless;
          // Register Idling to pump work queue — tests dispatch Revit API calls to main thread
          a.Idling += HeadlessRunner.OnIdling;
          // End suppression once the run finishes (complete / failed / crashed within the run),
          // even if this Revit session is left running rather than killed by the automation script.
          a.Idling += OnIdling_HeadlessCleanup;
          Log("Registered Idling for headless work queue dispatch");
        }
        else
        {
          Log("Registering Idling for UI mode");
          a.Idling += OnIdling_LaunchUI;
        }

        Log("OnStartup completed OK");
        return Result.Succeeded;
      }
      catch (Exception ex)
      {
        Log($"OnStartup ERROR: {ex}");
        return Result.Failed;
      }
    }

    private void OnApplicationInitialized_Headless(object sender, Autodesk.Revit.DB.Events.ApplicationInitializedEventArgs e)
    {
      Log("ApplicationInitialized (headless) fired");
      try
      {
        Application app = sender as Application;
        Log($"App version: {app?.VersionNumber}");
        DialogSuppression.Begin(); // whole headless session is the run
        HeadlessRunner.Launch(app, Runner.Config);
        Log("HeadlessRunner.Launch completed");
      }
      catch (Exception ex)
      {
        Log($"Headless ERROR: {ex}");
      }
    }

    /// <summary>
    /// Ends dialog/warning suppression once the headless run has finished, restoring Revit to
    /// normal handling. Runs on the main thread (Idling) and unhooks itself.
    /// </summary>
    private void OnIdling_HeadlessCleanup(object sender, Autodesk.Revit.UI.Events.IdlingEventArgs e)
    {
      if (!HeadlessRunner.TestsComplete) return;

      DialogSuppression.End();
      _uiCtrlApp.Idling -= OnIdling_HeadlessCleanup;
      Log("Headless cleanup: suppression ended, Revit restored to normal state.");
    }

    private void OnIdling_LaunchUI(object sender, Autodesk.Revit.UI.Events.IdlingEventArgs e)
    {
      if (_uiLaunched) return;
      _uiLaunched = true;
      _uiCtrlApp.Idling -= OnIdling_LaunchUI;

      Log("Idling (UI) fired");
      try
      {
        var uiapp = sender as UIApplication;
        Log($"UIApplication: {uiapp != null}");

        if (uiapp != null && Runner.Config.autoStart)
        {
          Log("Calling Runner.Launch...");
          Runner.Launch(uiapp);
          Log("Runner.Launch completed OK");
        }
      }
      catch (Exception ex)
      {
        Log($"UI launch ERROR: {ex}");
      }
    }

    public Result OnShutdown(UIControlledApplication a)
    {
      Log("OnShutdown called");
      DialogSuppression.End(); // final safety net — never leave suppression active
      return Result.Succeeded;
    }
  }
}
