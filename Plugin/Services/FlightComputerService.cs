// Plugin/Services/FlightComputerService.cs
using System;
using Plugin.Utils;
using Sandbox.ModAPI;
using VRageMath;

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

        public void DrawBrakingTunnel(IMyShipController ship)
        {
            if (ship == null || ship.CubeGrid.IsStatic) return;

            double velocity = ship.GetShipSpeed();
            if (velocity < 1.0) return;

            float maxDeceleration = _physics.CalculateMaxDeceleration(ship);

            // Stopping Distance formula: s = v^2 / (2 * a)
            double stoppingDistance = (velocity * velocity) / (2 * maxDeceleration);

            // Determine color based on collision prediction
            Color tunnelColor = GetSafetyColor(ship, stoppingDistance);

            // This now matches: (IMyShipController, double, Color, float)
            // RenderUtils.DrawTunnel(ship, stoppingDistance, tunnelColor, _config.Data.TunnelTransparency);
            RenderUtils.DrawTunnel(
                    ship,
                    stoppingDistance,
                    tunnelColor,
                    _config.Data.TunnelTransparency,
                    _config.Data.TunnelScale,
                    _config.Data.TunnelMaterial,
                    _config.Data.TunnelLineThickness
                );
        }

        private Color GetSafetyColor(IMyShipController ship, double dist)
        {
            if (_physics.IsCollisionImminent(ship, dist * 0.5)) return Color.Red;
            if (_physics.IsCollisionImminent(ship, dist)) return Color.Orange;
            return Color.Green;
        }
    }
}