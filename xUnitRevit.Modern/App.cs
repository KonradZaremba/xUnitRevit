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

        // Suppress Revit dialog boxes that can block headless/automated runs
        a.ControlledApplication.FailuresProcessing += (s, args) =>
        {
          args.GetFailuresAccessor().DeleteAllWarnings();
        };
        a.DialogBoxShowing += OnDialogBoxShowing;
        Log("Dialog suppression registered");

        if (Runner.Config.headless)
        {
          Log("Registering ApplicationInitialized for headless mode");
          a.ControlledApplication.ApplicationInitialized += OnApplicationInitialized_Headless;
          // Register Idling to pump work queue — tests dispatch Revit API calls to main thread
          a.Idling += HeadlessRunner.OnIdling;
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

    private void OnDialogBoxShowing(object sender, Autodesk.Revit.UI.Events.DialogBoxShowingEventArgs e)
    {
      Log($"Dialog suppressed: {e.DialogId}");
      // Auto-accept/dismiss all dialogs to prevent blocking automated runs
      if (e is Autodesk.Revit.UI.Events.TaskDialogShowingEventArgs taskDialog)
      {
        // For "load addon" dialogs, accept. For others, cancel.
        var id = taskDialog.DialogId ?? "";
        if (id.Contains("Load") || id.Contains("Trust") || id.Contains("Always"))
          taskDialog.OverrideResult((int)Autodesk.Revit.UI.TaskDialogResult.Ok);
        else
          taskDialog.OverrideResult((int)Autodesk.Revit.UI.TaskDialogResult.Close);
      }
      else
      {
        e.OverrideResult(1); // IDOK — accept
      }
    }

    private void OnApplicationInitialized_Headless(object sender, Autodesk.Revit.DB.Events.ApplicationInitializedEventArgs e)
    {
      Log("ApplicationInitialized (headless) fired");
      try
      {
        Application app = sender as Application;
        Log($"App version: {app?.VersionNumber}");
        HeadlessRunner.Launch(app, Runner.Config);
        Log("HeadlessRunner.Launch completed");
      }
      catch (Exception ex)
      {
        Log($"Headless ERROR: {ex}");
      }
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
      return Result.Succeeded;
    }
  }
}
