using SelfDrivingCar.Entities;
using System;

namespace AutoAutomobile
{
    public class AutoCarAction
    {
        public AutoCarAction(AutoCommandType commandType, TimeSpan delay = default, int? commandForce = null)
        {
            CommandType = commandType;
            Delay = delay;
            CommandForce = commandForce;
        }

        public AutoCommandType CommandType { get; }
        public int? CommandForce { get; }
        public TimeSpan Delay { get; }

        public CarAction ToCarAction() => CommandType switch
        {
            AutoCommandType.Delay => new CarAction { Action = AutoCommandType.Accelerate.ToString(), Force = 0 },
            AutoCommandType.IgnitionOn => new CarAction { Action = CommandType.ToString() },
            AutoCommandType.IgnitionOff => new CarAction { Action = CommandType.ToString() },
            _ => new CarAction { Action = CommandType.ToString(), Force = CommandForce },
        };
    }
}