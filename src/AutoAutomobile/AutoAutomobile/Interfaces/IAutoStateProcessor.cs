using SelfDrivingCar.Entities;
using System.Collections.Generic;

namespace AutoAutomobile
{
    internal interface IAutoStateProcessor
    {
        IEnumerable<AutoCarAction> GetCarActions(Car carState, Road roadState, double timeSafetyMargin = 0);
    }
}