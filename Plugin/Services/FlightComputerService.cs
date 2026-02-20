// Plugin/Services/FlightComputerService.cs
using System;
using Plugin.Utils;
using Sandbox.ModAPI;
using VRageMath;
using VRage.Game.ModAPI;

namespace Plugin.Services
{
    public class FlightComputerService
    {
        private readonly PhysicsService _physics;
        private readonly ConfigService _config;

        public FlightComputerService(PhysicsService physics, ConfigService config)
        {
            _physics = physics;
            _config = config;
        }

        public bool DrawBrakingTunnel(IMyShipController ship)
        {
            if (ship == null || ship.CubeGrid.IsStatic) return false;

            double velocity = ship.GetShipSpeed();
            if (velocity < 1.0) return false;

            // Calculate stopping distance using central physics logic
            float stoppingDistance = _physics.CalculateStoppingDistance(ship);

            // Check for collision within the calculated braking path
            bool hasCollision = CheckCollision(ship, stoppingDistance);

            // Determine color based on proximity of the hit
            Color tunnelColor = GetSafetyColor(ship, stoppingDistance);

            // Render the 3D visual guide
            RenderUtils.DrawTunnel(
                    ship,
                    stoppingDistance,
                    tunnelColor,
                    _config.Data.TunnelTransparency,
                    _config.Data.TunnelScale,
                    _config.Data.TunnelMaterial,
                    _config.Data.TunnelLineThickness
                );

            return hasCollision;
        }

        public bool CheckCollision(IMyShipController ship, float stopDist)
        {
            if (ship == null || stopDist <= 0) return false;

            Vector3D start = ship.GetPosition();
            // Extend check by 10% for a safety buffer
            Vector3D end = start + (ship.WorldMatrix.Forward * (stopDist * 1.1f));

            IHitInfo hit;
            // Detect if anything is obstructing the path (Voxels or Grids)
            if (MyAPIGateway.Physics.CastRay(start, end, out hit))
            {
                return true;
            }
            return false;
        }

        private Color GetSafetyColor(IMyShipController ship, double dist)
        {
            // Immediate danger (half of stopping distance)
            if (_physics.IsCollisionImminent(ship, dist * 0.5)) return Color.Red;

            // Potential danger (full stopping distance)
            if (_physics.IsCollisionImminent(ship, dist)) return Color.Orange;

            // Clear path
            return Color.Green;
        }
    }
}