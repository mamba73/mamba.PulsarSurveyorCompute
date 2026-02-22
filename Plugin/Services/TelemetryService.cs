// Plugin/Services/TelemetryService.cs
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace Plugin.Services
{
    public class TelemetryService
    {
        private readonly ConfigService _configService;

        public TelemetryService(ConfigService configService)
        {
            _configService = configService;
        }

        /// <summary>
        /// Returns true terrain altitude above nearest planet surface.
        /// Uses GetClosestSurfacePointGlobal — actual terrain geometry, not sea-level.
        /// Returns -1 in deep space (no planet nearby).
        /// </summary>
        public double GetAltitude(IMyShipController ship)
        {
            var planet = MyGamePruningStructure.GetClosestPlanet(ship.GetPosition());
            if (planet == null) return -1;

            Vector3D pos     = ship.GetPosition();
            Vector3D surface = planet.GetClosestSurfacePointGlobal(ref pos);
            return Vector3D.Distance(pos, surface);
        }

        /// <summary>
        /// Returns current natural gravity strength in Gs (Earth = 1.0G).
        /// Returns 0.0 in deep space.
        /// </summary>
        public float GetGravityG(IMyShipController ship)
        {
            double len = ship.GetNaturalGravity().Length();
            return (float)(len / 9.81); // 9.81 m/s² = 1G
        }

        /// <summary>
        /// Returns true when the ship is inside a planet's gravity influence zone.
        /// Zone radius = planet.AverageRadius * Config.PlanetDetectionMultiplier.
        ///
        /// Side effects:
        ///   - Fires a red HUD notification when altitude is below 2000m and speed > 100 m/s.
        ///
        /// This method is called every tick from MainPlugin and keeps all planet-related
        /// warnings centralized here rather than scattered across services.
        /// </summary>
        public bool UpdatePlanetData(IMyShipController ship)
        {
            var planet = MyGamePruningStructure.GetClosestPlanet(ship.GetPosition());
            if (planet == null) return false;

            double distToCenter = Vector3D.Distance(ship.GetPosition(), planet.PositionComp.GetPosition());

            // PlanetDetectionMultiplier defines how far out the "gravity zone" extends
            if (distToCenter >= planet.AverageRadius * _configService.Data.PlanetDetectionMultiplier)
                return false;

            // Inside gravity zone — compute true altitude
            Vector3D pos     = ship.GetPosition();
            Vector3D surface = planet.GetClosestSurfacePointGlobal(ref pos);
            double altitude  = Vector3D.Distance(pos, surface);

            // Low-altitude + high-speed warning (visible even when tunnel is not)
            if (altitude < 2000 && ship.GetShipSpeed() > 100)
            {
                MyAPIGateway.Utilities.ShowNotification(
                    $"WARNING: Low Altitude {altitude:N0}m | Speed {ship.GetShipSpeed():N0} m/s",
                    16,
                    VRage.Game.MyFontEnum.Red);
            }

            return true;
        }
    }
}
