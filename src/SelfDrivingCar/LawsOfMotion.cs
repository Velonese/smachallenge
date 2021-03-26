using System;
using System.Numerics;

namespace SelfDrivingCar
{
    public class LawsOfMotion
    {
        public static double GetFinalVelocity(double initialVelocity, double acceleration, double elapsedTime)
        {
            // v = u + a*t
            return initialVelocity + acceleration * elapsedTime;
        }

        public static double GetTimeToZeroVelocity(double initialVelocity, double acceleration)
        {
            // t = (v - u)/a
            return (0 - initialVelocity) / acceleration;
        }

        public static double GetTimeToMaxVelocity(double initialVelocity, double acceleration, int maxVelocity)
        {
            // t = (v - u)/a
            return (maxVelocity - initialVelocity) / acceleration;
        }

        public static double GetDistanceTravelled(double initalVelocity, double finalVelocity, double elapsedTime)
        {
            // s = t*(u + v)/2
            return elapsedTime * (initalVelocity + finalVelocity) / 2;
        }

        public static double GetDistanceToBrakingPoint(double initialVelocity, double accelerationRate, double decelerationRate, double finalVelocity, double totalDistance)
        {
            if (accelerationRate == decelerationRate)
            {
                return double.NaN;
            }
            return -(((2 * decelerationRate * totalDistance) + initialVelocity * initialVelocity - finalVelocity * finalVelocity) / (2 * accelerationRate - 2 * decelerationRate));
        }

        public static double GetTimeElapsedDuringAcceleration(double distance, double initialVelocity, double acceleration)
        {
            if (distance == 0 || acceleration == 0)
            {
                // no time would elapse.
                return 0;
            }
            Complex temp = Complex.Sqrt(2 * acceleration * distance + initialVelocity * initialVelocity);
            Complex root1 = -((temp + initialVelocity) / acceleration);
            Complex root2 = ((temp - initialVelocity) / acceleration);
            // return the positive time, or if both are positive, the smallest one that works:
            if (root1.Real > 0 && root2.Real > 0)
            {
                return Math.Min(root1.Real, root2.Real);
            }
            return Math.Max(root1.Real, root2.Real);
        }
    }
}