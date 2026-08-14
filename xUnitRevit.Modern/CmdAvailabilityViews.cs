using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace xUnitRevit
{
  internal class CmdAvailabilityViews : IExternalCommandAvailability
  {
    public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories)
    {
      return true;
    }
  }
}
