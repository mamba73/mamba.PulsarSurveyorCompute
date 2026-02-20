// Plugin/Services/TelemetryService.cs
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
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

        /// <summary>
        /// Updates telemetry data when the ship is within the gravitational influence of a planet.
        /// </summary>
        public void UpdatePlanetData(IMyShipController ship)
        {
            // Find the closest planet using the game pruning structure
            var planet = MyGamePruningStructure.GetClosestPlanet(ship.GetPosition());
            if (planet == null) return;

            // Calculate distance to the center and compare with detection multiplier from config
            double distToCenter = Vector3D.Distance(ship.GetPosition(), planet.PositionComp.GetPosition());
            float avgRadius = planet.AverageRadius;

            if (distToCenter < avgRadius * _config.Data.PlanetDetectionMultiplier)
            {
                // Get the closest point on the surface to determine true altitude
                Vector3D shipPos = ship.GetPosition();
                Vector3D surfacePoint = planet.GetClosestSurfacePointGlobal(ref shipPos);
                double altitude = Vector3D.Distance(shipPos, surfacePoint);

                // Calculate natural gravity in Gs
                float gravityG = (float)(ship.GetNaturalGravity().Length() / 9.81);

                // If altitude is low, trigger a warning notification (placeholder for HUD logic)
                if (altitude < 2000 && ship.GetShipSpeed() > 100)
                {
                    MyAPIGateway.Utilities.ShowNotification($"WARNING: Low Altitude! {altitude:N0}m", 16, VRage.Game.MyFontEnum.Red);
                }

                // Note: These values will be passed to the HudDisplayService in the next step
            }
        }
    }
}