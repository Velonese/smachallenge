using Microsoft.Extensions.Logging;
using SelfDrivingCar;
using SelfDrivingCar.Entities;
using System;
using System.Collections.Generic;

namespace AutoAutomobile
{
    internal class AutoStateProcessor : IAutoStateProcessor
    {
        private const double safeBrakingSpeed = 24; // braking @ 6m/s will cover < 50 meters.
        private const double brakingForce = 6d;
        private ILogger<AutoStateProcessor> logger;

        public AutoStateProcessor(ILogger<AutoStateProcessor> logger)
        {
            this.logger = logger;
        }

        /// <summary>
        /// Retrieves a collection of actions to be executed against the API based on incoming state.
        /// </summary>
        /// <param name="carState">The current state of the car, from API data.</param>
        /// <param name="roadState">The current state of the road, from API data.</param>
        /// <param name="timeSafetyMargin">A margin of error padding for latency purposes.</param>
        /// <returns>An enumeration representing between 1 and 4 <see cref="AutoCarAction"/> objects.</returns>
        public IEnumerable<AutoCarAction> GetCarActions(Car carState, Road roadState, double timeSafetyMargin = 0)
        {
            // The timeSafetyMargin helps compensate for the fact that these API calls take time.
            // Shut the car off if we have reached the end of the course.
            if (carState == null || roadState == null || IsEndOfTheRoad(carState, roadState))
            {
                logger.LogInformation("Executing vehicle shutdown.");
                // Just shut the car off in case of emergency, or if we are at the end of our journey.
                yield return new AutoCarAction(AutoCommandType.IgnitionOff);
                yield break;
            }

            // Set some values for future evaluation:
            double initialVelocity = carState.CurrentVelocity.GetValueOrDefault();
            var velocityDelta = roadState.CurrentSpeedLimit.Max.GetValueOrDefault() - initialVelocity;
            if (!roadState.SpeedLimitAhead.Max.HasValue)
            {
                // Since the future is looking null, we should decelerate and stop.
                velocityDelta = -initialVelocity;
            }

            // Decelerate if we are overspeed:
            if (velocityDelta < 0) // we are overspeed, or we are heading towards a null block - we need to immediately slow down.
            {
                logger.LogWarning("We are overspeed at {velocity} vs max of {maximum}", initialVelocity, roadState.CurrentSpeedLimit.Max.GetValueOrDefault());
                yield return new AutoCarAction(AutoCommandType.Brake, TimeSpan.Zero, (int)brakingForce);

                // Stop slowing down once we spend enough time stopping.
                yield return new AutoCarAction(AutoCommandType.Accelerate, TimeSpan.FromSeconds(Math.Abs(velocityDelta / brakingForce) - timeSafetyMargin), 0);

                // This is enough for now.
                yield break;
            }

            // Handle stop sign acceleration
            if (velocityDelta == 0 && initialVelocity == 0) // Can we reliably check for "Idling" here?
            {
                logger.LogInformation("Sucessful stop detected, proceeding from full stop.");
                // We are stopped - if speedLimitAhead were null, we would already have acted.
                // Set the current speed delta to the future speed limit, to encourage the vehicle to go.
                velocityDelta = roadState.SpeedLimitAhead.Max.GetValueOrDefault();
            }

            // Check to ensure we aren't caught at a bad time - right before the end of an enforcement block, for example:
            if (initialVelocity != 0 && roadState.SpeedLimitAhead.RemainingDistanceToEnforcement.HasValue &&
                roadState.SpeedLimitAhead.RemainingDistanceToEnforcement.Value / initialVelocity < .5)
            {
                logger.LogWarning("Assessed the state with {remaining} distance remaining at {velocity}, timing may be incorrect.", roadState.SpeedLimitAhead.RemainingDistanceToEnforcement.Value, initialVelocity);
                yield return new AutoCarAction(
                    AutoCommandType.Accelerate,
                    TimeSpan.FromSeconds(roadState.SpeedLimitAhead.RemainingDistanceToEnforcement.Value / initialVelocity),
                    0);
            }

            // Determine acceleration needs, deceleration needs, and reconcile the two:
            (double accelerationForce, double accelerationEnd) = CalculateAccelerationNeeded(velocityDelta);
            var distanceBeforeEnforcement = roadState.SpeedLimitAhead.RemainingDistanceToEnforcement.GetValueOrDefault();
            var enforcementTargetSpeed = roadState.SpeedLimitAhead.Max.GetValueOrDefault() == 0 ? safeBrakingSpeed : roadState.SpeedLimitAhead.Max.Value;
            var maxSpeed = initialVelocity + accelerationForce * accelerationEnd;
            (double brakingStart, double brakingEnd) = CalculateBrakingNeeded(initialVelocity, accelerationForce, maxSpeed, distanceBeforeEnforcement, enforcementTargetSpeed);

            // Reconcile the data we gathered to decide which events to return:
            double delayOffset = 0;
            if (accelerationForce > 0)
            {
                // Issue the request to accelerate immediately.
                yield return new AutoCarAction(AutoCommandType.Accelerate, TimeSpan.Zero, (int)accelerationForce);

                if (accelerationEnd < brakingStart)
                {
                    // We will reach close to max speed and don't want to go over.
                    yield return new AutoCarAction(AutoCommandType.Accelerate, TimeSpan.FromSeconds(accelerationEnd - timeSafetyMargin), 0);
                    delayOffset = accelerationEnd;
                }
            }
            if (brakingStart < double.PositiveInfinity)
            {
                // We need to hit the brakes at some point.
                yield return new AutoCarAction(AutoCommandType.Brake, TimeSpan.FromSeconds(brakingStart - delayOffset), (int)brakingForce);
                delayOffset = brakingStart;

                // Coast as we reach the end of the braking period.
                yield return new AutoCarAction(AutoCommandType.Accelerate, TimeSpan.FromSeconds(brakingEnd - delayOffset + timeSafetyMargin), 0);
                // We are done with this branch.
                yield break;
            }
            if (accelerationForce != 0) // we accelerated, and started coasting, now delay until coasting completes.
            {
                var accelerationDistance = LawsOfMotion.GetDistanceTravelled(initialVelocity, maxSpeed, accelerationEnd);
                var remainingDistance = distanceBeforeEnforcement - accelerationDistance;
                var coastingTime = remainingDistance / maxSpeed;
                yield return new AutoCarAction(AutoCommandType.Accelerate, TimeSpan.FromSeconds(coastingTime + timeSafetyMargin), 0);
            }
            else // No acceleration needed, and no braking required, coast for the entire duration of the block.
            {
                yield return new AutoCarAction(
                    AutoCommandType.Accelerate,
                    TimeSpan.FromSeconds(distanceBeforeEnforcement / initialVelocity + timeSafetyMargin),
                    0);
            }
            logger.LogInformation("All planned acceleration, braking a coasting actions for this block complete.");
        }

        // Checks if we are ready to shut down.
        private static bool IsEndOfTheRoad(Car carState, Road roadState)
        {
            return
                carState.CurrentVelocity.GetValueOrDefault() == 0 &&
                !roadState.SpeedLimitAhead.RemainingDistanceToEnforcement.HasValue;
        }

        // Calculates what braking is neede and when such braking should start based on current acceleration (if any)
        private (double brakingStart, double brakingEnd) CalculateBrakingNeeded(double currentVelocity, double accelerationForce, double maxVelocity, double distanceBeforeEnforcement, double enforcementTargetSpeed)
        {
            if (maxVelocity <= enforcementTargetSpeed)
            {
                // no braking is needed
                return (double.PositiveInfinity, double.PositiveInfinity);
            }
            var originalAccelerationTime = accelerationForce == 0 ? 0 : (maxVelocity - currentVelocity) / accelerationForce;
            var distanceAccelerating = LawsOfMotion.GetDistanceTravelled(currentVelocity, maxVelocity, originalAccelerationTime);
            var distanceDecelerating = LawsOfMotion.GetDistanceTravelled(maxVelocity, enforcementTargetSpeed, (maxVelocity - enforcementTargetSpeed) / brakingForce);

            if (distanceAccelerating + distanceDecelerating > distanceBeforeEnforcement)
            {
                // We would overshoot if we fully accelerate to speed.
                // We have to solve for the point within the acceleration at which we need to slow down.
                // Compare the time it takes to reach speed to the time it takes to stop at that speed to determine if we have an issue:
                var distanceUntilBraking = LawsOfMotion.GetDistanceToBrakingPoint(currentVelocity, accelerationForce, -brakingForce, enforcementTargetSpeed, distanceBeforeEnforcement);
                var timeSpentAccelerating = LawsOfMotion.GetTimeElapsedDuringAcceleration(distanceUntilBraking, currentVelocity, accelerationForce);
                var speedAcheievedBeforeBraking = timeSpentAccelerating * accelerationForce + currentVelocity;
                var timeSpentDecelerating = LawsOfMotion.GetTimeElapsedDuringAcceleration(distanceBeforeEnforcement - distanceUntilBraking, speedAcheievedBeforeBraking, -brakingForce);
                return (timeSpentAccelerating, timeSpentAccelerating + timeSpentDecelerating);
            }

            // figure out when we need to brake. Since they don't overlap, we can just calculate time investment from the distances.
            var accelerationTime = accelerationForce == 0 ? 0 : LawsOfMotion.GetTimeElapsedDuringAcceleration(distanceAccelerating, currentVelocity, accelerationForce);
            var decelerationTime = LawsOfMotion.GetTimeElapsedDuringAcceleration(distanceDecelerating, maxVelocity, -brakingForce);
            var coastingTime = (distanceBeforeEnforcement - (distanceAccelerating + distanceDecelerating)) / maxVelocity;

            return (accelerationTime + coastingTime, accelerationTime + decelerationTime + coastingTime);
        }

        // Gets the acceleration needed to achieve a give speed adjustment.
        private (double accelerationForce, double accelerationEnd) CalculateAccelerationNeeded(double currentSpeedDelta)
        {
            // We floor here to ensure a smooth cast to int, which is what the API accepts.
            double accelerationForce = Math.Floor(Math.Min(currentSpeedDelta, 6d));
            if (accelerationForce == 0)
            {
                return (0, 0);
            }
            double accelerationDuration = currentSpeedDelta / accelerationForce;
            return (accelerationForce, accelerationDuration);
        }
    }
}