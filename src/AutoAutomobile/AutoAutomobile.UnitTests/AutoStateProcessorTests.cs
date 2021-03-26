using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using SelfDrivingCar;
using SelfDrivingCar.Entities;
using System;
using System.Linq;
using Xunit;

namespace AutoAutomobile.UnitTests
{
    public class AutoStateProcessorTests
    {
        private readonly Mock<ILogger<AutoStateProcessor>> mockLogger;

        public AutoStateProcessorTests()
        {
            mockLogger = new Mock<ILogger<AutoStateProcessor>>();
        }

        [Fact]
        public void StateProcessor_GivenStartConditions_AcceleratesToSpeed()
        {
            // Arrange
            var processor = new AutoStateProcessor(mockLogger.Object);
            var testCarState = GetTestCarState(); // Defaults to on, idling, 0 m/s;
            var testRoadState = GetTestRoadState(20, 24, 96, 20, 24);
            // 96 meters with 24 m/s max will give us 4 seconds of acceleration at 12m/s, and 2 seconds of coast at 24m/s

            var expectedFirstAction = GetTestAutoCarAction(AutoCommandType.Accelerate, 6, TimeSpan.Zero);
            var expectedSecondAction = GetTestAutoCarAction(AutoCommandType.Accelerate, 0, TimeSpan.FromSeconds(4));
            var expectedThirdAction = GetTestAutoCarAction(AutoCommandType.Accelerate, 0, TimeSpan.FromSeconds(2));

            // Act
            var result = processor.GetCarActions(testCarState, testRoadState).ToArray();

            // Assert
            result.Should().HaveCount(3);
            result[0].Should().BeEquivalentTo(expectedFirstAction);
            result[1].Should().BeEquivalentTo(expectedSecondAction);
            result[2].Should().BeEquivalentTo(expectedThirdAction);
            SimulateExecution(0, result).distance.Should().Be(96);
        }

        [Fact]
        public void StateProcessor_GivenNullFutureBlockWithNoVelocity_SwitchesOffIgnition()
        {
            // Arrange
            var processor = new AutoStateProcessor(mockLogger.Object);
            var testCarState = GetTestCarState(); // Defaults to on, idling, 0 m/s;
            var testRoadState = GetTestRoadState(20, 24);

            var expectedFirstAction = GetTestAutoCarAction(AutoCommandType.IgnitionOff, null, TimeSpan.Zero);

            // Act
            var result = processor.GetCarActions(testCarState, testRoadState).ToArray();

            // Assert
            result.Should().HaveCount(1);
            result[0].Should().BeEquivalentTo(expectedFirstAction);
        }

        [Fact]
        public void StateProcessor_GivenNullFutureBlockWithVelocity_BrakesToAStop()
        {
            // Arrange
            var processor = new AutoStateProcessor(mockLogger.Object);
            var testCarState = GetTestCarState(currentVelocity: 6); 
            var testRoadState = GetTestRoadState(20, 24);

            var expectedFirstAction = GetTestAutoCarAction(AutoCommandType.Brake, 6, TimeSpan.Zero);
            var expectedSecondAction = GetTestAutoCarAction(AutoCommandType.Accelerate, 0, TimeSpan.FromSeconds(1));

            // Act
            var result = processor.GetCarActions(testCarState, testRoadState).ToArray();

            // Assert
            result.Should().HaveCount(2);
            result[0].Should().BeEquivalentTo(expectedFirstAction);
            result[1].Should().BeEquivalentTo(expectedSecondAction);
        }

        [Fact]
        public void StateProcessor_AlreadyAtIdealSpeed_Delays()
        {
            // Arrange
            var processor = new AutoStateProcessor(mockLogger.Object);
            var testCarState = GetTestCarState(currentVelocity: 24);
            var testRoadState = GetTestRoadState(20, 24, 24, 20, 24);
            // 24 meters at 24 m/s should be a delay of 1 second

            var expectedAction = GetTestAutoCarAction(AutoCommandType.Accelerate, 0, TimeSpan.FromSeconds(1));

            // Act
            var result = processor.GetCarActions(testCarState, testRoadState).ToArray();

            // Assert
            result.Should().ContainSingle();
            result.First().Should().BeEquivalentTo(expectedAction);
            SimulateExecution(24, result).distance.Should().Be(24);
        }

        [Fact]
        public void StateProcessor_ApproachingStop_SlowsToReasonableSpeed()
        {
            // Arrange
            var processor = new AutoStateProcessor(mockLogger.Object);
            var testCarState = GetTestCarState(currentVelocity: 48);
            var testRoadState = GetTestRoadState(20, 48, 300, 0, 0);

            var expectedFirstAction = GetTestAutoCarAction(AutoCommandType.Brake, 6, TimeSpan.FromSeconds(3.25));
            var expectedSecondAction = GetTestAutoCarAction(AutoCommandType.Accelerate, 0, TimeSpan.FromSeconds(4));

            // Act
            var result = processor.GetCarActions(testCarState, testRoadState).ToArray();

            // Assert
            result.Should().HaveCount(2);
            result[0].Should().BeEquivalentTo(expectedFirstAction);
            result[1].Should().BeEquivalentTo(expectedSecondAction);
            SimulateExecution(48, result).distance.Should().Be(300);
        }

        [Fact]
        public void StateProcessor_CannotReachMaxSpeed_BrakesToProperSpeed()
        {
            // Arrange
            var processor = new AutoStateProcessor(mockLogger.Object);
            var testCarState = GetTestCarState(currentVelocity: 12);
            var testRoadState = GetTestRoadState(40, 40, 140, 35, 35);

            var expectedFirstAction = GetTestAutoCarAction(AutoCommandType.Accelerate, 6, TimeSpan.FromSeconds(0));
            var expectedSecondAction = GetTestAutoCarAction(AutoCommandType.Brake, 6, TimeSpan.FromSeconds(3.25));
            var expectedThirdAction = GetTestAutoCarAction(AutoCommandType.Accelerate, 0, TimeSpan.FromSeconds(3.25));

            // Act
            var result = processor.GetCarActions(testCarState, testRoadState).ToArray();

            // Assert
            result.Should().HaveCount(3);
            SimulateDistance(12, result).Should().BeApproximately(140, 1);
            (var min, var max, var duration) = SimulateExecution(12, result);
            min.Should().BeGreaterOrEqualTo(12);
            max.Should().BeGreaterThan(20);
        }

        [Fact]
        public void StateProcessor_CannotReachMaxSpeed_BrakesAndSlowsAppropriatelyToProperSpeed()
        {
            // Arrange
            var processor = new AutoStateProcessor(mockLogger.Object);
            var testCarState = GetTestCarState(currentVelocity: 12);
            var testRoadState = GetTestRoadState(40, 40, 140, 15, 15);

            // Act
            var result = processor.GetCarActions(testCarState, testRoadState).ToArray();

            // Assert
            result.Should().HaveCount(3);
            (var min, var max, var distance) = SimulateExecution(12, result);
            SimulateDistance(12, result).Should().BeApproximately(140, 1);
            min.Should().BeGreaterOrEqualTo(12);
            max.Should().BeGreaterThan(20);
        }

        [Fact]
        public void StateProcessor_RoadCannotReachMaxSpeed_SpeedsUpAndBrakes()
        {
            // Arrange
            var processor = new AutoStateProcessor(mockLogger.Object);
            var testCarState = GetTestCarState(currentVelocity: 30);
            var testRoadState = GetTestRoadState(60, 60, 180, 30, 30); // Should start braking halfway through
            var approximateTimePerVelocityChange = LawsOfMotion.GetTimeElapsedDuringAcceleration(90, 30, 6);
            var expectedFirstAction = GetTestAutoCarAction(AutoCommandType.Accelerate, 6, TimeSpan.Zero);
            var expectedSecondAction = GetTestAutoCarAction(AutoCommandType.Brake, 6, TimeSpan.FromSeconds(approximateTimePerVelocityChange));
            var expectedThirdAction = GetTestAutoCarAction(AutoCommandType.Accelerate, 0, TimeSpan.FromSeconds(approximateTimePerVelocityChange));

            // Act
            var result = processor.GetCarActions(testCarState, testRoadState).ToArray();

            // Assert
            SimulateDistance(30, result[0], result[1]).Should().BeApproximately(90, 1);
            SimulateDistance(30, result).Should().BeApproximately(180, 1);
            result[0].Should().BeEquivalentTo(expectedFirstAction);
            result[1].Should().BeEquivalentTo(expectedSecondAction);
            result[2].Should().BeEquivalentTo(expectedThirdAction);
        }

        [Fact]
        public void StateProcessor_EnteringStopZone_StopsFully()
        {
            // Arrange
            var processor = new AutoStateProcessor(mockLogger.Object);
            var testCarState = GetTestCarState(currentVelocity: 24);
            var testRoadState = GetTestRoadState(0, 0, 50, 30, 25); // Should start braking halfway through
            var timeSpentBraking = LawsOfMotion.GetTimeToZeroVelocity(24, -6);
            var expectedFirstAction = GetTestAutoCarAction(AutoCommandType.Brake, 6, TimeSpan.Zero);
            var expectedSecondAction = GetTestAutoCarAction(AutoCommandType.Accelerate, 0, TimeSpan.FromSeconds(timeSpentBraking));

            // Act
            var result = processor.GetCarActions(testCarState, testRoadState).ToArray();

            // Assert
            result[0].Should().BeEquivalentTo(expectedFirstAction);
            result[1].Should().BeEquivalentTo(expectedSecondAction);
        }

        [Fact]
        public void StateProcessor_InStopZone_AcceleratesToExit()
        {
            // Arrange
            var processor = new AutoStateProcessor(mockLogger.Object);
            var testCarState = GetTestCarState(currentVelocity: 0);
            var testRoadState = GetTestRoadState(0, 0, 180, 25, 30); // Should start braking halfway through
            var expectedFirstAction = GetTestAutoCarAction(AutoCommandType.Accelerate, 6, TimeSpan.Zero);
            var expectedSecondAction = GetTestAutoCarAction(AutoCommandType.Accelerate, 0, TimeSpan.FromSeconds(5));

            // Act
            var result = processor.GetCarActions(testCarState, testRoadState).ToArray();

            // Assert
            result[0].Should().BeEquivalentTo(expectedFirstAction);
            result[1].Should().BeEquivalentTo(expectedSecondAction);
        }

        private double SimulateDistance(double initialVelocity = 0, params AutoCarAction[] actions)
        {
            double totalDistance = 0;
            double currentAcceleration = 0;
            double currentVelocity = initialVelocity;
            foreach (var action in actions)
            {
                if (action.Delay != TimeSpan.Zero)
                {
                    totalDistance += currentVelocity * action.Delay.TotalSeconds + (currentAcceleration / 2 * action.Delay.TotalSeconds * action.Delay.TotalSeconds);
                    currentVelocity += currentAcceleration * action.Delay.TotalSeconds;
                }
                if (action.CommandType == AutoCommandType.Accelerate)
                {
                    currentAcceleration = action.CommandForce.GetValueOrDefault();
                }
                else if (action.CommandType == AutoCommandType.Brake)
                {
                    currentAcceleration = -action.CommandForce.GetValueOrDefault();
                }
            }
            return totalDistance;
        }

        private (double minSpeed, double maxSpeed, double distance) SimulateExecution(double initialVelocity = 0, params AutoCarAction[] actions)
        {
            double totalDistance = 0;
            double currentAcceleration = 0;
            double currentVelocity = initialVelocity;
            double highestVelocity = 0;
            double minimumVelocity = initialVelocity;
            foreach (var action in actions)
            {
                if (action.Delay != TimeSpan.Zero)
                {
                    totalDistance += currentVelocity * action.Delay.TotalSeconds + (currentAcceleration / 2 * action.Delay.TotalSeconds * action.Delay.TotalSeconds);
                    currentVelocity += currentAcceleration * action.Delay.TotalSeconds;
                    if (currentVelocity > highestVelocity)
                    {
                        highestVelocity = currentVelocity;
                    }
                    if (currentVelocity < minimumVelocity)
                    {
                        minimumVelocity = currentVelocity;
                    }
                }
                if (action.CommandType == AutoCommandType.Accelerate)
                {
                    currentAcceleration = action.CommandForce.GetValueOrDefault();
                }
                else if (action.CommandType == AutoCommandType.Brake)
                {
                    currentAcceleration = -action.CommandForce.GetValueOrDefault();
                }
            }
            return (minimumVelocity, highestVelocity, totalDistance);
        }

        private static Car GetTestCarState(
            double currentVelocity = 0,
            string engineState = "Idling",
            string ignition = "On",
            double totalDistance = 0,
            double totalTime = 0) =>
            new Car
            {
                CurrentVelocity = currentVelocity,
                Engine = new Engine { State = engineState },
                Ignition = ignition,
                TotalDistanceTravelled = totalDistance,
                TotalTimeTravelled = totalTime
            };

        private static Road GetTestRoadState(
            int currentMin = 0,
            int currentMax = 0,
            double? distanceTillEnforcement = null,
            int? futureMin = null,
            int? futureMax = null) =>
            new Road
            {
                CurrentSpeedLimit = new SpeedLimit
                {
                    Max = currentMax,
                    Min = currentMin
                },
                SpeedLimitAhead = new SpeedLimitAhead
                {
                    Max = futureMax,
                    Min = futureMin,
                    RemainingDistanceToEnforcement = distanceTillEnforcement
                }
            };

        public AutoCarAction GetTestAutoCarAction(AutoCommandType action = AutoCommandType.Accelerate, int? force = null, TimeSpan delay = default) =>
            new AutoCarAction(action, delay, force);
    }
}