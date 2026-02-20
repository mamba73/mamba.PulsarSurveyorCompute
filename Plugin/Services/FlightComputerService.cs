using System;
using System.Collections.Generic;
using Plugin.Utils;
using Sandbox.ModAPI;
using VRageMath;

namespace Plugin.Services
{
    public class FlightComputerService
    {
        private readonly PhysicsService _physics;

        public FlightComputerService(PhysicsService physics)
        {
            _physics = physics;
        }

        public void DrawBrakingTunnel(IMyShipController ship)
        {
            if (ship == null) return;

            double velocity = ship.GetShipSpeed();
            if (velocity < 1.0) return;

            float maxDeceleration = _physics.CalculateMaxDeceleration(ship);
            // $s = v^2 / 2a$
            double stoppingDistance = (velocity * velocity) / (2 * maxDeceleration);

            RenderUtils.DrawTunnel(ship.WorldMatrix, stoppingDistance, GetSafetyColor(ship, stoppingDistance));
        }

        private Color GetSafetyColor(IMyShipController ship, double dist)
        {
            if (_physics.IsCollisionImminent(ship, dist * 0.5)) return Color.Red;
            if (_physics.IsCollisionImminent(ship, dist)) return Color.Orange;
            return Color.Green;
        }
    }
}