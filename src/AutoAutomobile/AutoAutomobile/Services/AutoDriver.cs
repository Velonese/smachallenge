using Microsoft.Extensions.Logging;
using SelfDrivingCar;
using SelfDrivingCar.Entities;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace AutoAutomobile
{
    internal class AutoDriver
    {
        private readonly ISelfDrivingCarService selfDrivingCarService;
        private readonly IAutoStateProcessor stateProcessor;
        private readonly ILogger<AutoDriver> logger;

        public AutoDriver(ISelfDrivingCarService selfDrivingCarService, IAutoStateProcessor stateProcessor, ILogger<AutoDriver> logger)
        {
            this.selfDrivingCarService = selfDrivingCarService;
            this.stateProcessor = stateProcessor;
            this.logger = logger;
        }

        internal async Task StartDrivingAsync(int course, string userEmail, int latencyCompesationMs = 50)
        {
            var token = selfDrivingCarService.Register(new TokenRequest
            {
                CourseLayout = course,
                Name = userEmail
            });

            selfDrivingCarService.GetCar();
            Stopwatch stopwatch = Stopwatch.StartNew();
            selfDrivingCarService.DoAction(new CarAction { Action = "IgnitionOn" });
            Car car = selfDrivingCarService.GetCar();
            Road road = selfDrivingCarService.GetRoad();
            int counter = 0;

            while (car.Ignition == "On" || road.SpeedLimitAhead.RemainingDistanceToEnforcement.HasValue)
            {
                logger.LogInformation(
                    "Section: {counter}, Current Speed:{speed}, CurrentLimit:{limit}, FutureLimit: {futureLimit}, EnforceDistance: {distance}",
                    counter++,
                    car.CurrentVelocity,
                    road.CurrentSpeedLimit.Max,
                    road.SpeedLimitAhead.Max,
                    road.SpeedLimitAhead.RemainingDistanceToEnforcement
                    );
                foreach (var autoAction in stateProcessor.GetCarActions(car, road, TimeSpan.FromMilliseconds(latencyCompesationMs).TotalSeconds))
                {
                    await Task.Delay(autoAction.Delay);
                    if (autoAction.CommandType == AutoCommandType.Delay)
                    {
                        continue;
                    }
                    logger.LogInformation("Taking action: {action} with force {commandForce}", autoAction.CommandType, autoAction.CommandForce);
                    selfDrivingCarService.DoAction(autoAction.ToCarAction());
                }
                stopwatch.Restart();
                car = selfDrivingCarService.GetCar();
                road = selfDrivingCarService.GetRoad();
                logger.LogInformation("refreshing Car and Road via API took {refreshMillis} ms", stopwatch.ElapsedMilliseconds);
            }
        }
    }
}