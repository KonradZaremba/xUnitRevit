using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace xUnitRevit
{
  /// <summary>
  /// Event invoker. Has a queue of actions that this handler iterates through.
  /// Required to run transactions from a non-modal window.
  /// Only used in UI mode (not headless).
  /// </summary>
  public class ExternalEventHandler : IExternalEventHandler
  {
    public bool Running = false;
    public List<Action> Queue { get; set; }

    public ExternalEventHandler(List<Action> queue)
    {
      Queue = queue;
    }

    public void Execute(UIApplication app)
    {
      Debug.WriteLine("Current queue len is: " + Queue.Count);
      if (Running) return;

      Running = true;

      Queue[0]();

      Queue.RemoveAt(0);
      Running = false;
    }

    public string GetName()
    {
      return "xUnit Revit";
    }
  }
}
