using System.Threading;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace xUnitRevit
{
  [Transaction(TransactionMode.Manual)]
  public class Command : IExternalCommand
  {
    public Result Execute(
      ExternalCommandData commandData,
      ref string message,
      ElementSet elements)
    {
      App.Log("Command.Execute called");
      try
      {
        UIApplication uiapp = commandData.Application;
        App.Log("Calling Runner.Launch from Command...");
        Runner.ReadConfig();
        Runner.Launch(uiapp);
        App.Log("Runner.Launch from Command completed OK");
        return Result.Succeeded;
      }
      catch (System.Exception ex)
      {
        App.Log($"Command ERROR: {ex}");
        message = ex.Message;
        return Result.Failed;
      }
    }
  }
}
