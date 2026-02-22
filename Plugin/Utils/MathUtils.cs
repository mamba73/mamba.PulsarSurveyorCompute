// Plugin/Utils/MathUtils.cs
using System;

namespace Plugin.Utils
{
    /// <summary>
    /// Stateless math helpers used across Pulsar services.
    /// No game API dependencies — pure arithmetic.
    /// </summary>
    public static class MathUtils
    {
        /// <summary>
        /// Minimum stopping distance: s = v² / (2a).
        /// Returns PositiveInfinity if deceleration is zero or negative (cannot brake).
        /// </summary>
        public static double GetStoppingDistance(double velocity, double acceleration)
        {
            if (acceleration <= 0) return double.PositiveInfinity;
            return (velocity * velocity) / (2.0 * acceleration);
        }

        /// <summary>Linear interpolation: returns a when t=0, b when t=1.</summary>
        public static double Lerp(double a, double b, double t)
            => a + (b - a) * t;

        /// <summary>Clamps value to [min, max] inclusive.</summary>
        public static double Clamp(double value, double min, double max)
            => Math.Max(min, Math.Min(max, value));
    }
}
