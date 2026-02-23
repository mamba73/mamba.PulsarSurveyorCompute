// Plugin/Services/InputHandlerService.cs
using System;
using System.Text;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using Plugin.Models;

namespace Plugin.Services
{
    public class InputHandlerService
    {
        private readonly Config _config;
        private readonly PhysicsService _physics;
        private readonly GpsManagerService _gpsManager;

        public InputHandlerService(Config config, PhysicsService physics, GpsManagerService gpsManager)
        {
            _config     = config;
            _physics    = physics;
            _gpsManager = gpsManager;
        }

        /// <summary>
        /// Called every tick. Handles all Pulsar keyboard shortcuts.
        ///   [T]         → Laser rangefinder ping
        ///   [Shift + T] → Clear all GPS markers and reset scan session
        /// </summary>
        public void Update(IMyShipController ship, ref double range)
        {
            bool tPressed = MyAPIGateway.Input.IsNewKeyPressed(VRage.Input.MyKeys.T);
            if (!tPressed) return;

            if (MyAPIGateway.Input.IsAnyShiftKeyPressed())
                _gpsManager.ClearAllMarkers();
            else
                PerformLaserScan(ship, out range);
        }

        /// <summary>
        /// Fires a raycast along the ship's forward vector up to Config.LaserMaxRange.
        ///
        /// RANGE: Controlled by Config.LaserMaxRange (default 50 000m / 50km).
        ///   Edit via config.xml → LaserMaxRange element.
        ///
        /// HIT-TYPE DISPATCH ORDER (order matters — planet check MUST be first):
        ///   1. Planet  → MyPlanet identified via 3-step check (see ResolvePlanet)
        ///   2. Voxel   → asteroid ore sampled at multiple depths
        ///   3. Grid    → ship/station contact GPS
        /// </summary>
        private void PerformLaserScan(IMyShipController ship, out double range)
        {
            range = -1;

            double maxRange = _config.LaserMaxRange; // from config — not hardcoded

            // Offset 5m forward to clear the ship's own collision bounding box
            Vector3D start = ship.WorldMatrix.Translation + ship.WorldMatrix.Forward * 5.0;
            Vector3D end   = start + ship.WorldMatrix.Forward * maxRange;

            IHitInfo hit;
            if (!MyAPIGateway.Physics.CastRay(start, end, out hit))
            {
                MyAPIGateway.Utilities.ShowNotification("[Pulsar] No target in range.", 2000, "White");
                return;
            }

            range = Vector3D.Distance(start, hit.Position);

            // ---------------------------------------------------------------
            // STEP 1: Try to resolve a planet at this hit position.
            //
            // WHY 3-STEP CHECK:
            //   In SE, hitting a planet surface returns a MyVoxelPhysics entity
            //   (the physics proxy child), not MyPlanet directly.
            //   GetTopMostParent() usually resolves it, but is not guaranteed
            //   across all SE versions and planet types.
            //   The position-based fallback (step 3) is the most reliable.
            // ---------------------------------------------------------------
            MyPlanet planet = ResolvePlanet(hit, hit.Position);

            if (planet != null)
            {
                HandlePlanetHit(planet, hit.Position);
                MyAPIGateway.Utilities.ShowNotification(
                    $"[Pulsar] Planet: {planet.Generator.Id.SubtypeName} ({range:N0}m)",
                    3000, "LightBlue");
                return;
            }

            // ---------------------------------------------------------------
            // STEP 2: Voxel (asteroid) — only reached if NOT a planet
            // ---------------------------------------------------------------
            IMyEntity hitEnt = hit.HitEntity?.GetTopMostParent();

            if (hitEnt is IMyVoxelBase voxel)
            {
                // Multi-depth sampling — penetrates stone shell to reach actual ore
                string ore = GpsManagerService.SampleOreAtDepths(
                    voxel, hit.Position, ship.WorldMatrix.Forward, _config.VoxelPenetrationDepths);

                if (ore != null && !ore.Equals("Stone", StringComparison.OrdinalIgnoreCase))
                {
                    _gpsManager.ProcessVoxelDetection(voxel, ore);
                    MyAPIGateway.Utilities.ShowNotification(
                        $"[Pulsar] Ore Lock: {ore} at {range:N0}m", 2000, "Yellow");
                }
                else
                {
                    MyAPIGateway.Utilities.ShowNotification(
                        $"[Pulsar] Rock/Stone at {range:N0}m — try closer or different angle", 2000, "White");
                }
                return;
            }

            // ---------------------------------------------------------------
            // STEP 3: Grid / ship — confirmed working, kept as-is
            // ---------------------------------------------------------------
            if (hitEnt is IMyCubeGrid grid)
            {
                string size = grid.GridSizeEnum == MyCubeSize.Large ? "Large" : "Small";
                _gpsManager.CreateGridGps(grid.DisplayName, hit.Position, "Detected", size, grid.EntityId);
                MyAPIGateway.Utilities.ShowNotification(
                    $"[Pulsar] Grid Lock: {grid.DisplayName} ({range:N0}m)", 2000, "White");
            }
        }

        /// <summary>
        /// Tries to identify a MyPlanet from a raycast hit using three escalating checks.
        ///
        /// Check 1 — Direct cast:
        ///   hit.HitEntity itself might already be MyPlanet (rare but possible).
        ///
        /// Check 2 — Parent chain:
        ///   hit.HitEntity is typically MyVoxelPhysics; GetTopMostParent() walks up
        ///   the entity tree to find MyPlanet.
        ///
        /// Check 3 — Position proximity (most reliable fallback):
        ///   GetClosestPlanet() finds the nearest planet regardless of entity hierarchy.
        ///   We confirm the hit is actually ON that planet by checking if the hit point
        ///   is within the planet's outer radius (AverageRadius * 1.2 for surface variation).
        ///
        /// Returns null if none of the three checks resolve a planet.
        /// </summary>
        private static MyPlanet ResolvePlanet(IHitInfo hit, Vector3D hitPos)
        {
            // Check 1: direct cast
            var direct = hit.HitEntity as MyPlanet;
            if (direct != null) return direct;

            // Check 2: walk parent chain
            var parent = hit.HitEntity?.GetTopMostParent() as MyPlanet;
            if (parent != null) return parent;

            // Check 3: position-based lookup — most reliable across SE versions
            var nearest = MyGamePruningStructure.GetClosestPlanet(hitPos);
            if (nearest == null) return null;

            // Confirm the hit point is within this planet's outer boundary
            // (AverageRadius * 1.5 covers hills, mountains, and atmosphere surface)
            double distToCenter = Vector3D.Distance(hitPos, nearest.PositionComp.GetPosition());
            if (distToCenter <= nearest.AverageRadius * 1.5)
                return nearest;

            return null;
        }

        /// <summary>
        /// Extracts all relevant planetary data and creates a GPS entry.
        ///
        /// GPS label format:
        ///   #Name (R:Xk) (G:X.XX) (GW:Xk) [(F+)]
        ///   R  = average radius in km
        ///   G  = surface gravity in Gs
        ///   GW = gravity well outer edge (~2× radius)
        ///   (F+) = fauna present
        ///
        /// GPS description contains: radius, gravity well, atmosphere, oxygen, fauna list.
        /// </summary>
        private void HandlePlanetHit(MyPlanet planet, Vector3D hitPos)
        {
            string name = planet.Generator.Id.SubtypeName;

            // Planet center: PositionLeftBottomCorner + half the voxel extent
            // SizeInMetres is Vector3 (float) — cast to Vector3D after float multiply
            Vector3D center = planet.PositionLeftBottomCorner + (Vector3D)(planet.SizeInMetres * 0.5f);

            float radiusKm      = planet.AverageRadius / 1000f;
            float gravity       = planet.Generator.SurfaceGravity;
            float gravityWellKm = (planet.AverageRadius * 2f) / 1000f;

            bool  hasAtmosphere = planet.HasAtmosphere;
            float oxygenLevel   = hasAtmosphere ? planet.Generator.Atmosphere.OxygenDensity : 0f;

            // ---- FAUNA DETECTION ----
            var faunaBuilder = new StringBuilder();
            var day   = planet.Generator.AnimalSpawnInfo;
            var night = planet.Generator.NightAnimalSpawnInfo;

            Action<MyPlanetAnimalSpawnInfo> addFauna = (info) =>
            {
                if (info?.Animals == null) return;
                foreach (var a in info.Animals)
                {
                    if (!faunaBuilder.ToString().Contains(a.AnimalType))
                    {
                        if (faunaBuilder.Length > 0) faunaBuilder.Append(", ");
                        faunaBuilder.Append(a.AnimalType);
                    }
                }
            };

            addFauna(day);
            addFauna(night);
            string faunaResult = faunaBuilder.Length > 0 ? faunaBuilder.ToString() : "None";

            _gpsManager.CreatePlanetGps(
                name, center, radiusKm, gravity, gravityWellKm,
                hasAtmosphere, oxygenLevel, faunaResult);
        }
    }
}
