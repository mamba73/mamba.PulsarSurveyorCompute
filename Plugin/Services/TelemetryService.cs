// Plugin/Services/TelemetryService.cs
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace Plugin.Services
{
    public class TelemetryService
    {
        private readonly ConfigService _configService;

        /// <summary>
        /// Cached planet approach data. Updated every PlanetRefreshTicks ticks.
        /// Read by HudDisplayService and GravityWellRenderer each frame.
        /// </summary>
        public PlanetApproachInfo CurrentApproach { get; private set; }

        /// <summary>All planets found in last full scan. Updated with CurrentApproach.</summary>
        public List<MyPlanet> NearbyPlanets { get; private set; } = new List<MyPlanet>();

        private int _refreshCounter = 0;

        // Planet cache — rebuilt every PlanetRefreshTicks
        private static List<MyPlanet> _planetCache = new List<MyPlanet>();
        private static int _planetCacheAge = int.MaxValue; // force first-tick refresh
        private const int PLANET_LIST_REFRESH = 18000;    // rebuild planet list every 5 min

        public TelemetryService(ConfigService configService)
        {
            _configService = configService;
        }

        /// <summary>True terrain altitude above nearest planet, or -1 in space.</summary>
        public double GetAltitude(IMyShipController ship)
        {
            var planet = CurrentApproach != null ? GetCachedPlanet(CurrentApproach.PlanetName) : null;
            if (planet == null) return -1;
            Vector3D pos     = ship.GetPosition();
            Vector3D surface = planet.GetClosestSurfacePointGlobal(ref pos);
            return Vector3D.Distance(pos, surface);
        }

        /// <summary>Natural gravity in Gs at current position (0 in space).</summary>
        public float GetGravityG(IMyShipController ship)
        {
            return (float)(ship.GetNaturalGravity().Length() / 9.81);
        }

        /// <summary>
        /// Throttled planet telemetry update.
        ///
        /// REFRESH RATE FIX:
        ///   Previously: GetEntities() called EVERY TICK (60×/s) — expensive.
        ///   The planet list was rebuilt every frame even though planets never move.
        ///
        ///   Now: Two-level caching:
        ///     1. Planet LIST (static objects) rebuilt every PLANET_LIST_REFRESH ticks (5 min).
        ///        Planets don't move or appear/disappear in normal gameplay.
        ///     2. Approach DATA (which planet, distances, sustainability) recomputed every
        ///        Config.PlanetRefreshTicks (default 1800 = 30s).
        ///        This is fast: iterates only the small cached planet list, no GetEntities().
        ///
        ///   Between refresh cycles: data comes from cache.
        ///   After a jump: next refresh will pick up the new planet within 30s.
        ///   Configurable: reduce PlanetRefreshTicks (e.g. to 300 = 5s) if you jump frequently.
        /// </summary>
        public bool UpdatePlanetData(IMyShipController ship, float liveMaxDecel)
        {
            // Rebuild planet list if stale (rarely needed)
            _planetCacheAge++;
            if (_planetCacheAge > PLANET_LIST_REFRESH)
                RebuildPlanetCache();

            // Only recompute approach every PlanetRefreshTicks
            _refreshCounter++;
            if (_refreshCounter < _configService.Data.PlanetRefreshTicks)
            {
                // Between refreshes: update gravity/sustainability with live data
                // (ship mass or thrust may have changed) but keep cached planet selection
                if (CurrentApproach != null)
                {
                    float gravAccel = (float)(ship.GetNaturalGravity().Length());
                    CurrentApproach.GravityAccel   = gravAccel;
                    CurrentApproach.CanEscapeGravity = liveMaxDecel > gravAccel;
                    CurrentApproach.LiveMaxDecel   = liveMaxDecel;
                    CurrentApproach.GravityG       = GetGravityG(ship);
                }
                return CurrentApproach != null;
            }
            _refreshCounter = 0;

            // Full recompute
            return RecomputeApproach(ship, liveMaxDecel);
        }

        /// <summary>
        /// Force immediate planet refresh. Call after hyperspace jump or teleport.
        /// </summary>
        public void ForceRefresh()
        {
            _refreshCounter = int.MaxValue; // triggers recompute on next Update
        }

        // -----------------------------------------------------------------------
        // PRIVATE
        // -----------------------------------------------------------------------

        private bool RecomputeApproach(IMyShipController ship, float liveMaxDecel)
        {
            if (_planetCache.Count == 0)
            {
                CurrentApproach = null;
                NearbyPlanets.Clear();
                return false;
            }

            Vector3D shipPos = ship.GetPosition();
            MyPlanet bestInWell   = null;
            double   bestSurfDist = double.MaxValue;
            MyPlanet bestNearest  = null;
            double   bestNearDist = double.MaxValue;

            NearbyPlanets.Clear();

            foreach (var planet in _planetCache)
            {
                if (planet == null || planet.Closed) continue;

                Vector3D center    = planet.PositionComp.GetPosition();
                double   distC     = Vector3D.Distance(shipPos, center);
                float    wellR     = planet.AverageRadius * 2f;

                Vector3D pos2    = shipPos;
                Vector3D surface = planet.GetClosestSurfacePointGlobal(ref pos2);
                double   surfD   = Vector3D.Distance(shipPos, surface);

                // Track all planets within 3× well radius as "nearby" (for gravity viz)
                if (distC < wellR * 3.0)
                    NearbyPlanets.Add(planet);

                if (surfD < bestNearDist) { bestNearDist = surfD; bestNearest = planet; }

                if (distC < wellR && surfD < bestSurfDist)
                { bestSurfDist = surfD; bestInWell = planet; }
            }

            var best = bestInWell ?? bestNearest;
            if (best == null) { CurrentApproach = null; return false; }

            Vector3D shipPos2    = ship.GetPosition();
            Vector3D planetCenter = best.PositionComp.GetPosition();
            double distToCenter  = Vector3D.Distance(shipPos2, planetCenter);
            float  wellRadius    = best.AverageRadius * 2f;
            double distToWell    = distToCenter - wellRadius;

            Vector3D surf2    = best.GetClosestSurfacePointGlobal(ref shipPos2);
            double   altitude = Vector3D.Distance(shipPos2, surf2);
            float    gravG    = GetGravityG(ship);
            float    gravAcc  = (float)(ship.GetNaturalGravity().Length());

            CurrentApproach = new PlanetApproachInfo
            {
                PlanetName       = best.Generator.Id.SubtypeName,
                AltitudeM        = altitude,
                GravityG         = gravG,
                SurfaceGravityG  = best.Generator.SurfaceGravity,
                DistToWellEdgeM  = distToWell,
                InsideGravityWell= distToWell < 0,
                CanEscapeGravity = liveMaxDecel > gravAcc,
                LiveMaxDecel     = liveMaxDecel,
                GravityAccel     = gravAcc
            };

            if (altitude < 2000 && ship.GetShipSpeed() > 100)
            {
                MyAPIGateway.Utilities.ShowNotification(
                    $"WARNING: Alt {altitude:N0}m | {ship.GetShipSpeed():N0} m/s",
                    16, VRage.Game.MyFontEnum.Red);
            }

            return true;
        }

        private static void RebuildPlanetCache()
        {
            _planetCache.Clear();
            var entities = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(entities);
            foreach (var ent in entities)
            {
                var p = ent as MyPlanet;
                if (p != null) _planetCache.Add(p);
            }
            _planetCacheAge = 0;
        }

        private static MyPlanet GetCachedPlanet(string name)
        {
            foreach (var p in _planetCache)
                if (p != null && !p.Closed && p.Generator.Id.SubtypeName == name) return p;
            return null;
        }

        /// <summary>Get the live MyPlanet object for nearby planet visualization.</summary>
        public static List<MyPlanet> GetPlanetCache() => _planetCache;
    }

    public class PlanetApproachInfo
    {
        public string PlanetName        { get; set; }
        public double AltitudeM         { get; set; }
        public float  GravityG          { get; set; }
        public float  SurfaceGravityG   { get; set; }
        public double DistToWellEdgeM   { get; set; }
        public bool   InsideGravityWell { get; set; }
        public bool   CanEscapeGravity  { get; set; }
        public float  LiveMaxDecel      { get; set; }
        public float  GravityAccel      { get; set; }
    }
}
