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
using Plugin.Services;

namespace Plugin.Services
{
    public class InputHandlerService
    {
        private readonly Config _config;
        private readonly PhysicsService _physics;
        private readonly GpsManagerService _gpsManager;

        public InputHandlerService(Config config, PhysicsService physics, GpsManagerService gpsManager)
        {
            _config    = config;
            _physics   = physics;
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
        /// Hit-type dispatch:
        ///   Planet (MyPlanet)    → planet GPS with radius, gravity, gravity-well, fauna
        ///   Asteroid (voxel)     → ore detected via multi-depth sampling, asteroid GPS
        ///   Grid (IMyCubeGrid)   → grid contact GPS with relation info
        ///
        /// FIX: Planet and voxel detection now both check for MyPlanet first,
        /// since MyPlanet IS a voxel map. Check order matters — planet before voxel.
        /// </summary>
        private void PerformLaserScan(IMyShipController ship, out double range)
        {
            range = -1;

            // FIX: Use config-driven LaserMaxRange — not hardcoded 50000
            double maxRange = _config.LaserMaxRange;

            // Offset 5m forward to safely clear the ship's own collision bounding box
            // (increased from 2.5m — large ship noses can extend further)
            Vector3D start = ship.WorldMatrix.Translation + ship.WorldMatrix.Forward * 5.0;
            Vector3D end   = start + ship.WorldMatrix.Forward * maxRange;

            IHitInfo hit;
            if (!MyAPIGateway.Physics.CastRay(start, end, out hit))
            {
                MyAPIGateway.Utilities.ShowNotification("[Pulsar] No target in range.", 2000, "White");
                return;
            }

            range = Vector3D.Distance(start, hit.Position);
            IMyEntity hitEnt = hit.HitEntity?.GetTopMostParent();
            if (hitEnt == null)
            {
                MyAPIGateway.Utilities.ShowNotification($"[Pulsar] Hit terrain ({range:N0}m)", 2000, "White");
                return;
            }

            // ---------------------------------------------------------------
            // PLANET CHECK — must come BEFORE generic voxel check
            // MyPlanet implements IMyVoxelBase; checking for planet first
            // prevents it from being misidentified as an asteroid.
            // ---------------------------------------------------------------
            var asPlanet = hitEnt as MyPlanet;
            if (asPlanet == null && hitEnt is IMyVoxelBase)
            {
                // TopMostParent returned IMyVoxelBase — cast to MyPlanet as secondary check
                asPlanet = hitEnt as MyPlanet;
            }

            if (asPlanet != null)
            {
                HandlePlanetHit(asPlanet, hit.Position);
                MyAPIGateway.Utilities.ShowNotification(
                    $"[Pulsar] Planet: {asPlanet.Generator.Id.SubtypeName} ({range:N0}m)",
                    3000, "LightBlue");
                return;
            }

            // ---------------------------------------------------------------
            // ASTEROID / VOXEL CHECK
            // At this point we know it's a voxel but NOT a planet.
            // ---------------------------------------------------------------
            if (hitEnt is IMyVoxelBase voxel)
            {
                // FIX: Multi-depth sampling replaces single 0.5m penetration.
                // Asteroid stone crusts can be several meters thick.
                string ore = GpsManagerService.SampleOreAtDepths(voxel, hit.Position, ship.WorldMatrix.Forward, _config.VoxelPenetrationDepths);

                if (ore != null && !ore.Equals("Stone", StringComparison.OrdinalIgnoreCase))
                {
                    _gpsManager.ProcessVoxelDetection(voxel, ore);
                    MyAPIGateway.Utilities.ShowNotification($"[Pulsar] Lock: {ore} at {range:N0}m", 2000, "Yellow");
                }
                else
                {
                    MyAPIGateway.Utilities.ShowNotification($"[Pulsar] Lock: Rock/Stone at {range:N0}m", 2000, "White");
                }
                return;
            }

            // ---------------------------------------------------------------
            // GRID / SHIP CHECK — confirmed working, kept as-is
            // ---------------------------------------------------------------
            if (hitEnt is IMyCubeGrid grid)
            {
                string size = grid.GridSizeEnum == MyCubeSize.Large ? "Large" : "Small";
                _gpsManager.CreateGridGps(grid.DisplayName, hit.Position, "Detected", size);
                MyAPIGateway.Utilities.ShowNotification($"[Pulsar] Grid Lock: {grid.DisplayName} ({range:N0}m)", 2000, "White");
            }
        }

        /// <summary>
        /// Extracts all relevant data from a MyPlanet instance and creates a
        /// comprehensive GPS entry. Called when the laser hits a planet surface.
        ///
        /// GPS label format: #Name (R:Xk) (G:X.XX) (GW:Xk) [(F+)]
        /// GPS description: radius, gravity well, atmosphere, oxygen, fauna list.
        /// </summary>
        private void HandlePlanetHit(MyPlanet planet, Vector3D hitPos)
        {
            string name = planet.Generator.Id.SubtypeName;

            // Geometric center of the planet for the GPS pin
            // PositionLeftBottomCorner + half of total voxel extent
            // SizeInMetres is Vector3 (float) — must multiply by float literal, not double
            Vector3D center = planet.PositionLeftBottomCorner + (Vector3D)(planet.SizeInMetres * 0.5f);

            // Radius in km (rounded to nearest km)
            float radiusKm = planet.AverageRadius / 1000f;

            // Surface gravity in Gs
            float gravity = planet.Generator.SurfaceGravity;

            // Gravity well — SE convention: gravity influence ends at ~2× average radius
            float gravityWellKm = (planet.AverageRadius * 2f) / 1000f;

            // Atmosphere
            bool hasAtmosphere = planet.HasAtmosphere;
            float oxygenLevel  = hasAtmosphere
                ? planet.Generator.Atmosphere.OxygenDensity
                : 0f;

            // ---- FAUNA DETECTION ----
            // Read both day and night spawn tables and merge unique animal types.
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

            // Delegate GPS creation to GpsManagerService (centralized GPS ownership)
            _gpsManager.CreatePlanetGps(
                name, center, radiusKm, gravity, gravityWellKm,
                hasAtmosphere, oxygenLevel, faunaResult);
        }
    }
}
