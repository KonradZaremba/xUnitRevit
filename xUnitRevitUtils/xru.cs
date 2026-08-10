using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace xUnitRevitUtils
{
  /// <summary>
  /// Utility class with methods and properties used by the xUnit Revit plugin.
  /// Supports both UI mode (with UIApplication) and headless mode (DB-only).
  /// </summary>
  public static class xru
  {
    public static UIApplication Uiapp { get; set; }
    public static Autodesk.Revit.ApplicationServices.Application App { get; set; }
    private static List<Action> Queue { get; set; }
    private static ExternalEvent EventHandler { get; set; }
    public static SynchronizationContext UiContext { get; set; }

    /// <summary>
    /// Whether the runner is operating in headless mode (no UI).
    /// </summary>
    public static bool IsHeadless { get; private set; }

    /// <summary>
    /// Optional diagnostic logger. In headless mode HeadlessRunner wires this to its log file
    /// so the dispatch/queue path can be traced without a circular project reference.
    /// </summary>
    public static Action<string> Logger { get; set; }

    private static void LogDiag(string message) => Logger?.Invoke(message);

    /// <summary>
    /// How long a headless main-thread dispatch waits before treating the work item as stuck.
    /// Generous enough to cover slow document opens/upgrades on a cold Revit.
    /// </summary>
    private const int MainThreadDispatchTimeoutSeconds = 120;

    /// <summary>
    /// Work queue for dispatching Revit API calls to the main thread in headless mode.
    /// Background threads post work here; the main thread pumps it.
    /// </summary>
    public static System.Collections.Concurrent.BlockingCollection<(Action work, System.Threading.ManualResetEventSlim done, Exception[] error)> HeadlessWorkQueue { get; private set; }

    /// <summary>
    /// Initialize for UI mode (traditional Revit add-in with full UI access).
    /// </summary>
    public static void Initialize(UIApplication uiapp, SynchronizationContext uiContext, ExternalEvent eventHandler, List<Action> queue)
    {
      IsHeadless = false;
      Uiapp = uiapp;
      App = uiapp.Application;
      UiContext = uiContext;
      EventHandler = eventHandler;
      Queue = queue;
    }

    /// <summary>
    /// Initialize for headless mode (DB-only, no UIApplication).
    /// Used with Revit Platform Services or automated testing.
    /// </summary>
    public static void InitializeHeadless(Autodesk.Revit.ApplicationServices.Application app)
    {
      IsHeadless = true;
      App = app;
      Uiapp = null;
      UiContext = null;
      EventHandler = null;
      Queue = null;
      HeadlessWorkQueue = new System.Collections.Concurrent.BlockingCollection<(Action, System.Threading.ManualResetEventSlim, Exception[])>();
    }

    /// <summary>
    /// Dispatches an action to the main thread and blocks until complete.
    /// Called from background threads to execute Revit API calls on the main thread.
    /// Works in both headless mode (via work queue) and UI mode (via SynchronizationContext).
    /// </summary>
    public static void DispatchToMainThread(Action action)
    {
      if (IsHeadless)
      {
        var done = new System.Threading.ManualResetEventSlim(false);
        var error = new Exception[1];
        HeadlessWorkQueue.Add((action, done, error));
        // Wait with a timeout so a stuck main-thread call surfaces as a diagnostic
        // instead of hanging the whole run indefinitely.
        if (!done.Wait(TimeSpan.FromSeconds(MainThreadDispatchTimeoutSeconds)))
        {
          LogDiag($"DispatchToMainThread: TIMEOUT after {MainThreadDispatchTimeoutSeconds}s waiting for main thread — work item never completed (Idling not draining queue, or the API call is blocked).");
          throw new TimeoutException($"Headless main-thread dispatch timed out after {MainThreadDispatchTimeoutSeconds}s. See log for details.");
        }
        if (error[0] != null)
          System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error[0]).Throw();
      }
      else
      {
        // UI mode — dispatch via SynchronizationContext
        UiContext.Send(_ => action(), null);
      }
    }

    #region utility methods

    /// <summary>
    /// Returns the selected elements in the active document.
    /// Only available in UI mode.
    /// </summary>
    public static List<Element> GetActiveSelection()
    {
      if (IsHeadless)
        throw new InvalidOperationException("GetActiveSelection is not available in headless mode.");

      Assert.NotNull(Uiapp);

      if (Uiapp.ActiveUIDocument != null)
        return Uiapp.ActiveUIDocument.Selection.GetElementIds().Select(x => Uiapp.ActiveUIDocument.Document.GetElement(x)).ToList();
      return new List<Element>();
    }

    /// <summary>
    /// Opens a document. In UI mode, opens and activates it. In headless mode, opens without UI.
    /// </summary>
    public static Document OpenDoc(string filePath)
    {
      if (IsHeadless)
      {
        Assert.NotNull(App);
        Document doc = null;
        DispatchToMainThread(() =>
        {
          doc = App.OpenDocumentFile(filePath);
        });
        // Let Revit finish processing the document open (updaters, events, etc.)
        // by yielding several Idling ticks before returning to the caller.
        for (int i = 0; i < 5; i++)
          DispatchToMainThread(() => { });
        Assert.NotNull(doc);
        return doc;
      }

      Assert.NotNull(Uiapp);
      Document doc2 = null;
      UiContext.Send(x => { doc2 = Uiapp.OpenAndActivateDocument(filePath).Document; }, null);
      Assert.NotNull(doc2);
      return doc2;
    }

    /// <summary>
    /// Creates a new empty document.
    /// </summary>
    public static Document CreateNewDoc(string templatePath, string filePath, bool overwrite = true)
    {
      try
      {
        if (overwrite && File.Exists(filePath))
          File.Delete(filePath);
      }
      catch { }

      if (IsHeadless)
      {
        Assert.NotNull(App);
        Document doc = null;
        DispatchToMainThread(() =>
        {
          if (!File.Exists(filePath))
          {
            doc = App.NewProjectDocument(templatePath);
            doc.SaveAs(filePath);
            doc.Close();
          }

          doc = App.OpenDocumentFile(filePath);
        });
        // Let Revit finish processing the document open
        for (int i = 0; i < 5; i++)
          DispatchToMainThread(() => { });
        Assert.NotNull(doc);
        return doc;
      }

      Assert.NotNull(Uiapp);
      Document doc2 = null;
      UiContext.Send(x =>
      {
        if (!File.Exists(filePath))
        {
          doc2 = Uiapp.Application.NewProjectDocument(templatePath);
          doc2.SaveAs(filePath);
          doc2.Close();
        }

        doc2 = Uiapp.OpenAndActivateDocument(filePath).Document;
      }, null);
      Assert.NotNull(doc2);
      return doc2;
    }

    /// <summary>
    /// Runs an Action in a Revit transaction.
    /// In UI mode, uses ExternalEvent queue. In headless mode, dispatches to main thread via work queue.
    /// </summary>
    public static Task RunInTransaction(Action action, Document doc, string transactionName = "transaction", bool ignoreWarnings = false)
    {
      if (IsHeadless)
      {
        return Task.Run(() =>
        {
          DispatchToMainThread(() =>
          {
            using (Transaction transaction = new Transaction(doc, transactionName))
            {
              transaction.Start();

              if (ignoreWarnings)
              {
                var options = transaction.GetFailureHandlingOptions();
                options.SetFailuresPreprocessor(new IgnoreAllWarnings());
                transaction.SetFailureHandlingOptions(options);
              }

              action.Invoke();
              transaction.Commit();
            }
          });
        });
      }

      var tcs = new TaskCompletionSource<string>();
      Queue.Add(new Action(() =>
      {
        try
        {
          using (Transaction transaction = new Transaction(doc, transactionName))
          {
            transaction.Start();

            if (ignoreWarnings)
            {
              var options = transaction.GetFailureHandlingOptions();
              options.SetFailuresPreprocessor(new IgnoreAllWarnings());
              transaction.SetFailureHandlingOptions(options);
            }

            action.Invoke();
            transaction.Commit();
          }
        }
        catch (Exception e)
        {
          tcs.TrySetException(e);
        }
        tcs.TrySetResult("");
      }));

      EventHandler.Raise();

      return tcs.Task;
    }

    /// <summary>
    /// Runs an Action. In UI mode, uses ExternalEvent queue. In headless mode, dispatches to main thread via work queue.
    /// </summary>
    public static Task Run(Action action, Document doc)
    {
      if (IsHeadless)
      {
        return Task.Run(() =>
        {
          DispatchToMainThread(() => action.Invoke());
        });
      }

      var tcs = new TaskCompletionSource<string>();
      Queue.Add(new Action(() =>
      {
        try
        {
          action.Invoke();
        }
        catch (Exception e)
        {
          tcs.TrySetException(e);
        }
        tcs.TrySetResult("");
      }));

      EventHandler.Raise();

      return tcs.Task;
    }

    /// <summary>
    /// A failures preprocesser that clears any failures that occur within a transaction
    /// </summary>
    internal class IgnoreAllWarnings : IFailuresPreprocessor
    {
      public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
      {
        var failList = failuresAccessor.GetFailureMessages();

        foreach (FailureMessageAccessor failure in failList)
        {
          failuresAccessor.DeleteWarning(failure);
        }

        return FailureProcessingResult.Continue;
      }
    }

    #endregion
  }
}
