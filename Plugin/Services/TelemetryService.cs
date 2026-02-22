// Plugin/Services/TelemetryService.cs
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace Plugin.Services
{
    /// <summary>
    /// Encapsulates all planetary telemetry calculations.
    /// Also holds the current PlanetApproachInfo computed each tick
    /// so HudDisplayService can render it without re-calculating.
    /// </summary>
    public class TelemetryService
    {
        private readonly ConfigService _configService;

        /// <summary>
        /// Updated every tick. Null when in open space (no planet nearby).
        /// Read by HudDisplayService to display approach data.
        /// </summary>
        public PlanetApproachInfo CurrentApproach { get; private set; }

        public TelemetryService(ConfigService configService)
        {
            _configService = configService;
        }

        /// <summary>
        /// Returns true terrain altitude above nearest planet surface.
        /// Returns -1 in deep space.
        /// </summary>
        public double GetAltitude(IMyShipController ship)
        {
            var planet = MyGamePruningStructure.GetClosestPlanet(ship.GetPosition());
            if (planet == null) return -1;

            Vector3D pos     = ship.GetPosition();
            Vector3D surface = planet.GetClosestSurfacePointGlobal(ref pos);
            return Vector3D.Distance(pos, surface);
        }

        /// <summary>Returns natural gravity in Gs (0 in space).</summary>
        public float GetGravityG(IMyShipController ship)
        {
            return (float)(ship.GetNaturalGravity().Length() / 9.81);
        }

        /// <summary>
        /// Core planetary telemetry update. Called every tick.
        /// Computes PlanetApproachInfo and fires low-altitude HUD warnings.
        /// Returns true when inside a planet's gravity influence zone.
        /// </summary>
        public bool UpdatePlanetData(IMyShipController ship, float liveMaxDecel)
        {
            var planet = MyGamePruningStructure.GetClosestPlanet(ship.GetPosition());
            if (planet == null)
            {
                CurrentApproach = null;
                return false;
            }

            Vector3D shipPos    = ship.GetPosition();
            Vector3D planetCenter = planet.PositionComp.GetPosition();
            double distToCenter = Vector3D.Distance(shipPos, planetCenter);
            float  avgRadius    = planet.AverageRadius;

            // Gravity well outer edge: ~2× average radius (SE convention)
            float gravityWellRadius = avgRadius * 2f;
            double distToWellEdge  = distToCenter - gravityWellRadius; // negative = inside well

            // Compute true altitude
            Vector3D surface  = planet.GetClosestSurfacePointGlobal(ref shipPos);
            double   altitude = Vector3D.Distance(shipPos, surface);

            float gravityG    = (float)(ship.GetNaturalGravity().Length() / 9.81);
            float surfaceGrav = planet.Generator.SurfaceGravity;
            string planetName = planet.Generator.Id.SubtypeName;

            // ---- GRAVITY SUSTAINABILITY CHECK ----
            // Can the ship out-thrust gravity to avoid being pulled down?
            // Gravity force (m/s²) vs available deceleration (m/s²).
            // If liveMaxDecel < gravityAccel, the ship cannot hover — it will fall.
            float gravityAccel      = (float)(ship.GetNaturalGravity().Length());
            bool  canEscapeGravity  = liveMaxDecel > gravityAccel;

            CurrentApproach = new PlanetApproachInfo
            {
                PlanetName       = planetName,
                AltitudeM        = altitude,
                GravityG         = gravityG,
                SurfaceGravityG  = surfaceGrav,
                DistToWellEdgeM  = distToWellEdge,
                InsideGravityWell = distToWellEdge < 0,
                CanEscapeGravity  = canEscapeGravity,
                LiveMaxDecel     = liveMaxDecel,
                GravityAccel     = gravityAccel
            };

            // --- LOW ALTITUDE FAST APPROACH WARNING ---
            if (altitude < 2000 && ship.GetShipSpeed() > 100)
            {
                MyAPIGateway.Utilities.ShowNotification(
                    $"WARNING: Low Altitude {altitude:N0}m | Speed {ship.GetShipSpeed():N0} m/s",
                    16, VRage.Game.MyFontEnum.Red);
            }

            return true;
        }
    }

    /// <summary>
    /// Data transfer object holding one frame of planetary approach telemetry.
    /// Computed in TelemetryService, rendered in HudDisplayService.
    /// </summary>
    public class PlanetApproachInfo
    {
        public string PlanetName        { get; set; }
        public double AltitudeM         { get; set; }
        public float  GravityG          { get; set; }
        public float  SurfaceGravityG   { get; set; }
        /// <summary>Positive = outside well (meters to edge). Negative = inside well (depth inside).</summary>
        public double DistToWellEdgeM   { get; set; }
        public bool   InsideGravityWell { get; set; }
        public bool   CanEscapeGravity  { get; set; }
        public float  LiveMaxDecel      { get; set; }
        public float  GravityAccel      { get; set; }
    }
}
