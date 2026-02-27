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
        /// <summary>
        /// Returns distance (m) to nearest obstacle, or -1 if clear.
        /// HUD uses this for the "Xm to impact" countdown.
        /// </summary>
        public double DrawBrakingTunnel(IMyShipController ship)
        {
            if (ship == null || ship.CubeGrid.IsStatic) return -1;

            double velocity = ship.GetShipSpeed();
            if (velocity < _configService.Data.MinSpeedForTunnel) return -1;

            // CS0128 guard: stoppingDistance declared ONCE
            float stoppingDistance = _physics.CalculateStoppingDistance(ship);
            if (stoppingDistance <= 0) return -1;

            var cfg = _configService.Data;

            // Check for obstacles at orange and red threshold distances.
            // Thresholds are multiples of stopping distance — configurable.
            //   OrangeThreshold (default 1.5) = warn at 1.5× stopping distance
            //   RedThreshold    (default 0.6) = brake-now at 0.6× stopping distance
            double orangeDist = stoppingDistance * cfg.TunnelOrangeThreshold;
            double redDist    = stoppingDistance * cfg.TunnelRedThreshold;

            double collisionDist = _physics.NearestObstacleDistance(ship, orangeDist);
            bool   inOrange      = collisionDist >= 0 && collisionDist <= orangeDist;
            bool   inRed         = collisionDist >= 0 && collisionDist <= redDist;

            Color tunnelColor = inRed    ? Color.Red
                              : inOrange ? Color.Orange
                              :            Color.Green;

            VRageMath.Vector3D halfExt = PhysicsService.GetConnectedHalfExtent(ship.CubeGrid)
                                        + cfg.CollisionMargin;

            RenderUtils.DrawTunnel(
                ship,
                orangeDist,   // tunnel extends to orange threshold distance
                tunnelColor,
                cfg.TunnelTransparency,
                halfExt.X,
                halfExt.Y,
                cfg.TunnelMaterial,
                cfg.TunnelLineThickness,
                cfg.TunnelRingSpacing
            );

            return collisionDist; // -1 = clear, positive = meters to obstacle
        }

    }
}
