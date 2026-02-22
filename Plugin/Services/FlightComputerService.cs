// Plugin/Services/FlightComputerService.cs
using Plugin.Utils;
using Sandbox.ModAPI;
using VRageMath;
using VRage.Game.ModAPI;

namespace Plugin.Services
{
    public class FlightComputerService
    {
        private readonly PhysicsService _physics;
        private readonly ConfigService _configService;

        public FlightComputerService(PhysicsService physics, ConfigService configService)
        {
            _physics       = physics;
            _configService = configService;
        }

        /// <summary>
        /// Renders the predictive braking tunnel and evaluates the collision status.
        ///
        /// Tunnel color:
        ///   Green  → clear path (no obstacle within full stopping distance)
        ///   Orange → caution (obstacle within full stopping distance)
        ///   Red    → imminent (obstacle within 50% of stopping distance = must brake now)
        ///
        /// Returns true when a collision is detected — used by AudioService and HUD.
        /// </summary>
        public bool DrawBrakingTunnel(IMyShipController ship)
        {
            if (ship == null || ship.CubeGrid.IsStatic) return false;

            // MinSpeedForTunnel from config — not hardcoded
            double velocity = ship.GetShipSpeed();
            if (velocity < _configService.Data.MinSpeedForTunnel) return false;

            // FIX (CS0128 guard): stoppingDistance declared ONCE here.
            // Previous versions had a duplicate declaration that prevented compilation.
            float stoppingDistance = _physics.CalculateStoppingDistance(ship);
            if (stoppingDistance <= 0) return false;

            bool hasCollision = _physics.IsCollisionImminent(ship, stoppingDistance);
            Color tunnelColor = GetSafetyColor(ship, stoppingDistance);

            RenderUtils.DrawTunnel(
                ship,
                stoppingDistance,
                tunnelColor,
                _configService.Data.TunnelTransparency,
                _configService.Data.TunnelScale,
                _configService.Data.TunnelMaterial,
                _configService.Data.TunnelLineThickness
            );

            return hasCollision;
        }

        /// <summary>
        /// Evaluates two distance thresholds and returns the appropriate warning color.
        ///   50% of stopping distance → Red (must brake NOW)
        ///   100% of stopping distance → Orange (braking zone entered)
        ///   Clear → Green
        /// </summary>
        private Color GetSafetyColor(IMyShipController ship, double fullStopDist)
        {
            if (_physics.IsCollisionImminent(ship, fullStopDist * 0.5)) return Color.Red;
            if (_physics.IsCollisionImminent(ship, fullStopDist))       return Color.Orange;
            return Color.Green;
        }
    }
}
