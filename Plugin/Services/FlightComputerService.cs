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
        private readonly ConfigService  _configService;

        public FlightComputerService(PhysicsService physics, ConfigService configService)
        {
            _physics       = physics;
            _configService = configService;
        }

        /// <summary>
        /// Renders the animated braking tunnel and evaluates collision status.
        ///
        /// Tunnel color:
        ///   Green  → clear path
        ///   Orange → obstacle within full stopping distance
        ///   Red    → obstacle within 50% of stopping distance (brake NOW)
        ///
        /// Tunnel appearance:
        ///   Rings scroll toward the ship (position-based animation).
        ///   Near rings are more opaque; far rings fade to near-invisible.
        ///   Base alpha from Config.TunnelTransparency (default 0.12 — subtle).
        ///
        /// Returns true when a collision is imminent — drives AudioService and HUD warning.
        /// </summary>
        public bool DrawBrakingTunnel(IMyShipController ship)
        {
            if (ship == null || ship.CubeGrid.IsStatic) return false;

            double velocity = ship.GetShipSpeed();
            if (velocity < _configService.Data.MinSpeedForTunnel) return false;

            // CS0128 guard: stoppingDistance declared ONCE
            float stoppingDistance = _physics.CalculateStoppingDistance(ship);
            if (stoppingDistance <= 0) return false;

            bool  hasCollision = _physics.IsCollisionImminent(ship, stoppingDistance);
            Color tunnelColor  = GetSafetyColor(ship, stoppingDistance);

            // Use actual combined grid half-extents + collision margin for ring size.
            // This ensures rings visually match the swept volume used for collision detection.
            VRageMath.Vector3D halfExt = PhysicsService.GetConnectedHalfExtent(ship.CubeGrid)
                                        + _configService.Data.CollisionMargin;

            RenderUtils.DrawTunnel(
                ship,
                stoppingDistance,
                tunnelColor,
                _configService.Data.TunnelTransparency,
                halfExt.X,   // halfWidth  (X = right extent)
                halfExt.Y,   // halfHeight (Y = up extent)
                _configService.Data.TunnelMaterial,
                _configService.Data.TunnelLineThickness,
                _configService.Data.TunnelRingSpacing
            );

            return hasCollision;
        }

        private Color GetSafetyColor(IMyShipController ship, double fullStopDist)
        {
            if (_physics.IsCollisionImminent(ship, fullStopDist * 0.5)) return Color.Red;
            if (_physics.IsCollisionImminent(ship, fullStopDist))       return Color.Orange;
            return Color.Green;
        }
    }
}
