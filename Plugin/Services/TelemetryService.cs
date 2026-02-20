// Plugin/Services/TelemetryService.cs
using Sandbox.ModAPI;
using Sandbox.Game.Entities;
using VRage.Game.ModAPI;
using VRageMath;

namespace Plugin.Services
{
    public class TelemetryService
    {
        private readonly ConfigService _config;

        public TelemetryService(ConfigService config)
        {
            _config = config;
        }

        public void UpdatePlanetData(IMyShipController ship)
        {
            var planet = MyGamePruningStructure.GetClosestPlanet(ship.GetPosition());
            if (planet == null) return;

            double distToCenter = Vector3D.Distance(ship.GetPosition(), planet.PositionComp.GetPosition());
            float avgRadius = planet.AverageRadius;

            if (distToCenter < avgRadius * _config.Data.PlanetDetectionMultiplier)
            {
                // Telemetry logic
            }
        }
    }
}