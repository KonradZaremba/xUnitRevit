using System;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace xUnitRevit
{
  /// <summary>
  /// Run-scoped suppression of Revit dialogs and transaction warnings.
  ///
  /// Active ONLY between <see cref="Begin"/> and <see cref="End"/> — i.e. for the duration of a test
  /// run, in BOTH headless and interactive/UI modes. It is never active while Revit sits idle in an
  /// interactive session: these handlers are global and destructive to normal use (DialogBoxShowing
  /// auto-answers EVERY TaskDialog, so other add-ins' questions get an instant "cancelled" and appear
  /// dead; FailuresProcessing silently deletes warnings from every transaction the user makes). See the
  /// "nothing global outside a run" invariant in CLAUDE.md.
  ///
  /// Idempotent: repeated Begin()/End() calls are safe. Runs never nest (each runner guards on a
  /// single in-progress flag), so a simple active-flag is sufficient.
  /// </summary>
  internal static class DialogSuppression
  {
    private static UIControlledApplication _app;
    private static EventHandler<FailuresProcessingEventArgs> _failuresHandler;
    private static bool _active;

    /// <summary>Wire up the application whose events are (de)registered. Call once at startup.</summary>
    public static void Configure(UIControlledApplication app) => _app = app;

    /// <summary>Start suppressing dialogs/warnings for a run. Idempotent.</summary>
    public static void Begin()
    {
      if (_active || _app == null) return;
      _failuresHandler = (s, e) => e.GetFailuresAccessor().DeleteAllWarnings();
      _app.ControlledApplication.FailuresProcessing += _failuresHandler;
      _app.DialogBoxShowing += OnDialogBoxShowing;
      _active = true;
      App.Log("DialogSuppression: begin (run scope)");
    }

    /// <summary>Stop suppressing — restore Revit's normal dialog/warning handling. Idempotent.</summary>
    public static void End()
    {
      if (!_active) return;
      try
      {
        _app.DialogBoxShowing -= OnDialogBoxShowing;
        _app.ControlledApplication.FailuresProcessing -= _failuresHandler;
      }
      catch (Exception ex)
      {
        App.Log($"DialogSuppression.End error: {ex.Message}");
      }
      _failuresHandler = null;
      _active = false;
      App.Log("DialogSuppression: end (Revit restored to normal state)");
    }

    private static void OnDialogBoxShowing(object sender, DialogBoxShowingEventArgs e)
    {
      App.Log($"Dialog suppressed: {e.DialogId}");
      if (e is TaskDialogShowingEventArgs taskDialog)
      {
        // Accept "load add-in / trust" prompts; dismiss everything else.
        var id = taskDialog.DialogId ?? "";
        if (id.Contains("Load") || id.Contains("Trust") || id.Contains("Always"))
          taskDialog.OverrideResult((int)TaskDialogResult.Ok);
        else
          taskDialog.OverrideResult((int)TaskDialogResult.Close);
      }
      else
      {
        e.OverrideResult(1); // IDOK — accept
      }
    }
  }
}
