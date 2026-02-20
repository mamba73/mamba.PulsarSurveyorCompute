using System;
using VRageMath;

namespace Plugin.Utils
{
    public static class MathUtils
    {
        public static double GetStoppingDistance(double velocity, double acceleration)
        {
            if (acceleration <= 0) return double.PositiveInfinity;
            return (velocity * velocity) / (2 * acceleration);
        }
    }
}