// Plugin/Services/GpsManagerService.cs
using System;
using System.Collections.Generic;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Voxels;
using VRageMath;
using Plugin.Models;

namespace Plugin.Services
{
    public class GpsManagerService
    {
        private readonly Config _config;

        // -----------------------------------------------------------------------
        // ASTEROID CACHE
        // Key: voxel.EntityId — guarantees one GPS entry per physical asteroid body.
        // The GPS pin is placed at the asteroid's geometric center (WorldAABB.Center),
        // NOT at the player position at scan time.
        // -----------------------------------------------------------------------
        private readonly Dictionary<long, ResourceMarker> _asteroidCache
            = new Dictionary<long, ResourceMarker>();

        private int _scanDelay    = 0;
        private int _asteroidCounter = 0;

        /// <summary>Sector label prefix. Editable via Terminal textbox. E.g. "S01"</summary>
        public string CurrentSectorName = "S01";

        /// <summary>
        /// Pulsar's independent scan range (meters).
        /// Separate from the vanilla Ore Detector block range cap (~150m).
        /// Initialized from Config.PulsarScanRange. Updated by the terminal slider.
        /// </summary>
        public float PulsarScanRange;

        public GpsManagerService(Config config)
        {
            _config        = config;
            PulsarScanRange = config.PulsarScanRange;
        }

        // -----------------------------------------------------------------------
        // PUBLIC API
        // -----------------------------------------------------------------------

        /// <summary>
        /// Auto-scan throttled to ~2 seconds. Called every tick from MainPlugin.
        /// Uses entity-based voxel discovery — not raycasting — so no asteroid is
        /// skipped due to step size or ray direction.
        /// </summary>
        public void ScanForVoxels(IMyShipController ship)
        {
            if (_scanDelay++ < 120) return; // ~2s at 60 TPS
            _scanDelay = 0;
            ExecuteEntityScan(ship.WorldMatrix.Translation);
        }

        /// <summary>
        /// Immediate full scan from the given detector block's position.
        /// Called by the Terminal button and G-menu action.
        /// </summary>
        public void ForceSectorScan(IMyTerminalBlock detectorBlock)
        {
            int before = _asteroidCache.Count;
            ExecuteEntityScan(detectorBlock.WorldMatrix.Translation);
            int found = _asteroidCache.Count - before;

            MyAPIGateway.Utilities.ShowNotification(
                $"[Pulsar] Scan complete — {found} new deposits ({_asteroidCache.Count} total, range {PulsarScanRange:N0}m).",
                4000, MyFontEnum.Green);
        }

        /// <summary>
        /// Scans the entire game entity list for planets and creates GPS for each.
        /// Unlike asteroids, planets are global fixed objects — we just iterate all entities.
        /// Called by the Terminal "Scan All Planets" button.
        /// </summary>
        public void ScanAllPlanets()
        {
            int found = 0;
            var entities = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(entities);

            foreach (var ent in entities)
            {
                var planet = ent as MyPlanet;
                if (planet == null) continue;

                string name = planet.Generator.Id.SubtypeName;
                Vector3D center = planet.PositionLeftBottomCorner + (Vector3D)(planet.SizeInMetres * 0.5f);
                float radiusKm      = planet.AverageRadius / 1000f;
                float gravity       = planet.Generator.SurfaceGravity;
                float gravityWellKm = (planet.AverageRadius * 2f) / 1000f;
                bool  hasAtmosphere = planet.HasAtmosphere;
                float oxygenLevel   = hasAtmosphere ? planet.Generator.Atmosphere.OxygenDensity : 0f;
                string fauna        = BuildFaunaString(planet);

                CreatePlanetGps(name, center, radiusKm, gravity, gravityWellKm,
                                hasAtmosphere, oxygenLevel, fauna);
                found++;
            }

            MyAPIGateway.Utilities.ShowNotification(
                $"[Pulsar] Planet scan complete — {found} planet(s) mapped.", 4000, MyFontEnum.Green);
        }

        /// <summary>
        /// Removes all Pulsar GPS markers and resets the scan session. Triggered by Shift+T.
        /// </summary>
        public void ClearAllMarkers()
        {
            foreach (var marker in _asteroidCache.Values)
                if (marker.Gps != null)
                    MyAPIGateway.Session.GPS.RemoveLocalGps(marker.Gps);

            _asteroidCache.Clear();
            _asteroidCounter = 0;

            MyAPIGateway.Utilities.ShowNotification(
                "[Pulsar] Survey reset. All markers cleared.", 3000, MyFontEnum.Green);
        }

        // -----------------------------------------------------------------------
        // DETECTION REGISTRATION
        // -----------------------------------------------------------------------

        /// <summary>
        /// Records an ore detection on a known voxel body.
        /// - First detection: creates GPS at the asteroid's geometric center (WorldAABB.Center).
        /// - Subsequent detections: appends ore name to existing marker.
        /// - Duplicate: silently ignored.
        /// </summary>
        public void ProcessVoxelDetection(IMyVoxelBase voxel, string oreName)
        {
            long entityId = voxel.EntityId;

            if (!_asteroidCache.ContainsKey(entityId))
            {
                _asteroidCounter++;
                string asteroidId = $"{CurrentSectorName} A{_asteroidCounter:D2}";
                Vector3D center   = voxel.WorldAABB.Center; // asteroid center, not player position

                var marker = new ResourceMarker
                {
                    EntityId = entityId,
                    Position = center,
                    OreName  = oreName,
                    Title    = asteroidId
                };
                _asteroidCache[entityId] = marker;
                SyncGps(marker);
            }
            else if (!_asteroidCache[entityId].OreName.Contains(oreName))
            {
                _asteroidCache[entityId].OreName += $", {oreName}";
                SyncGps(_asteroidCache[entityId]);
            }
        }

        // -----------------------------------------------------------------------
        // GPS CREATION
        // -----------------------------------------------------------------------

        /// <summary>
        /// Creates/updates a planet GPS entry.
        /// Label: #Name (R:Xk) (G:X.XX) (GW:Xk) [(F+)]
        /// </summary>
        public void CreatePlanetGps(
            string name, Vector3D pos, float radiusKm, float gravity,
            float gravityWellKm, bool hasAtmosphere, float oxygenLevel, string faunaInfo)
        {
            bool   hasFauna  = !string.IsNullOrEmpty(faunaInfo) && faunaInfo != "None";
            string faunaTag  = hasFauna ? " (F+)" : "";
            string label     = $"#{name} (R:{radiusKm:F0}k) (G:{gravity:F2}) (GW:{gravityWellKm:F0}k){faunaTag}";
            string atmoLine  = hasAtmosphere ? $"Yes (O2:{oxygenLevel:F2})" : "None";
            string desc      = $"Radius: {radiusKm:F0} km | GW: {gravityWellKm:F0} km\n"
                             + $"Surface Gravity: {gravity:F2} G\n"
                             + $"Atmosphere: {atmoLine}\n"
                             + $"Fauna: {(hasFauna ? faunaInfo : "None detected")}";

            // Remove stale duplicate before recreating
            var existing = new List<IMyGps>();
            MyAPIGateway.Session.GPS.GetGpsList(MyAPIGateway.Session.Player.IdentityId, existing);
            foreach (var g in existing)
                if (g.Name == label) MyAPIGateway.Session.GPS.RemoveLocalGps(g);

            var gps = MyAPIGateway.Session.GPS.Create(label, desc, pos, true);
            gps.GPSColor = Color.LightBlue;
            MyAPIGateway.Session.GPS.AddLocalGps(gps);
        }

        /// <summary>
        /// Creates a GPS marker for a detected grid, with duplicate guard (100m).
        /// </summary>
        public void CreateGridGps(string name, Vector3D pos, string relation, string size)
        {
            string label = $"[Grid] {name} ({size})";

            var existing = new List<IMyGps>();
            MyAPIGateway.Session.GPS.GetGpsList(MyAPIGateway.Session.Player.IdentityId, existing);
            foreach (var g in existing)
                if (g.Name == label && Vector3D.Distance(g.Coords, pos) < 100) return;

            var gps = MyAPIGateway.Session.GPS.Create(label, $"Relation: {relation}", pos, true);
            gps.GPSColor = (relation == "Enemies" || relation == "Hostile") ? Color.Red : Color.White;
            MyAPIGateway.Session.GPS.AddLocalGps(gps);
        }

        // -----------------------------------------------------------------------
        // INTERNAL SCAN ENGINE
        // -----------------------------------------------------------------------

        /// <summary>
        /// Entity-based asteroid scan. Uses GetEntitiesInSphere to find all voxel maps
        /// in PulsarScanRange, then scans each asteroid's voxel storage directly.
        ///
        /// WHY entity-based and not raycasting:
        ///   Raycasting with large steps (50m) misses small asteroids entirely.
        ///   Entity discovery finds EVERY asteroid in range regardless of its size or
        ///   the scan direction. Once an asteroid entity is found, its storage is
        ///   sampled in a 3D grid to find ore veins.
        ///
        /// Filter: IMyVoxelMap (asteroids only) — MyPlanet does NOT implement IMyVoxelMap.
        /// This is the key type distinction: planets are excluded at the interface level.
        /// </summary>
        private void ExecuteEntityScan(Vector3D origin)
        {
            var sphere   = new BoundingSphereD(origin, PulsarScanRange);
            var entities = MyAPIGateway.Entities.GetEntitiesInSphere(ref sphere);

            foreach (var ent in entities)
            {
                // IMyVoxelMap is ONLY asteroids — MyPlanet does NOT implement this interface.
                // This is cleaner than casting to MyPlanet and inverting.
                var voxelMap = ent as IMyVoxelMap;
                if (voxelMap == null) continue;

                // Skip already-fully-scanned asteroids (all known ores recorded)
                // Allow re-scan to pick up ores missed in previous scan
                ScanVoxelStorage(voxelMap);
            }

            // Also scan for grids in range
            ExecuteGridScan(origin);
        }

        /// <summary>
        /// Scans a single asteroid's voxel storage to find ore veins.
        ///
        /// Algorithm:
        ///   1. Read the storage in a 3D grid with stride = Config.VoxelScanStride (default 8m).
        ///   2. At each grid point, first read Content to skip empty space (fast).
        ///   3. For solid voxels, read Material and check if it's non-Stone.
        ///   4. Report each unique ore to ProcessVoxelDetection.
        ///
        /// WHY this approach works when raycasting failed:
        ///   - No dependency on ray direction or step size
        ///   - Covers the ENTIRE asteroid volume systematically
        ///   - Content pre-check skips empty cells cheaply
        ///   - Stride 8m reliably finds ore veins (SE ore veins are typically 10-100m wide)
        ///
        /// Performance: Worst-case 512×512×512 asteroid at stride 8 = 64³ ≈ 262k cells.
        ///   With content pre-check, only ~5-10% are solid (ore-bearing), so ~13-26k
        ///   Material reads. Acceptable for a 2-second throttled scan.
        /// </summary>
        private void ScanVoxelStorage(IMyVoxelMap voxelMap)
        {
            if (voxelMap.Storage == null) return;
            var storage = (VRage.Game.Voxels.IMyStorage)voxelMap.Storage;
            Vector3I storageSize = storage.Size;

            int   stride    = Math.Max(1, _config.VoxelScanStride);
            var   cache     = new MyStorageData();
            var   foundOres = new HashSet<string>(); // ores found in THIS scan pass

            for (int x = 0; x < storageSize.X; x += stride)
            for (int y = 0; y < storageSize.Y; y += stride)
            for (int z = 0; z < storageSize.Z; z += stride)
            {
                Vector3I cell = new Vector3I(x, y, z);

                try
                {
                    // --- PASS 1: Content check (skip empty/air voxels cheaply) ---
                    cache.Resize(cell, cell);
                    storage.ReadRange(cache, MyStorageDataTypeFlags.Content, 0, cell, cell);
                    if (cache.Content(0) < 64) continue; // Less than 25% solid = skip

                    // --- PASS 2: Material read on solid voxels ---
                    cache.Resize(cell, cell);
                    storage.ReadRange(cache, MyStorageDataTypeFlags.Material, 0, cell, cell);

                    byte matIdx = cache.Material(0);
                    if (matIdx == byte.MaxValue) continue; // undefined material

                    var matDef = MyDefinitionManager.Static.GetVoxelMaterialDefinition(matIdx);
                    if (matDef == null) continue;

                    string ore = matDef.MinedOre;
                    if (ore.Equals("Stone", StringComparison.OrdinalIgnoreCase)) continue;

                    // New ore found on this asteroid
                    if (foundOres.Add(ore)) // Add returns false if already in set
                        ProcessVoxelDetection(voxelMap, ore);
                }
                catch
                {
                    // ReadRange can throw on unloaded/corrupted chunks — silently skip
                }
            }
        }

        /// <summary>
        /// Sphere-scans for grids in range. Excludes own ship and tiny debris.
        /// </summary>
        private void ExecuteGridScan(Vector3D origin)
        {
            var sphere   = new BoundingSphereD(origin, PulsarScanRange);
            var entities = MyAPIGateway.Entities.GetEntitiesInSphere(ref sphere);

            foreach (var ent in entities)
            {
                var grid = ent as IMyCubeGrid;
                if (grid == null) continue;
                if (grid.WorldVolume.Radius <= 2.0f) continue; // debris

                // Check if this is the player's own ship
                var player = MyAPIGateway.Session.Player;
                var controlled = player?.Controller?.ControlledEntity?.Entity as IMyCubeGrid;
                if (controlled != null && grid.EntityId == controlled.EntityId) continue;

                string size     = grid.GridSizeEnum == MyCubeSize.Large ? "Large" : "Small";
                long   ownerId  = grid.BigOwners?.Count > 0 ? grid.BigOwners[0] : 0;
                string relation = ownerId != 0
                    ? player?.GetRelationTo(ownerId).ToString() ?? "Unknown"
                    : "Neutral";

                CreateGridGps(grid.DisplayName, grid.WorldMatrix.Translation, relation, size);
            }
        }

        // -----------------------------------------------------------------------
        // GPS SYNC
        // -----------------------------------------------------------------------

        private void SyncGps(ResourceMarker marker)
        {
            if (marker.Gps != null)
                MyAPIGateway.Session.GPS.RemoveLocalGps(marker.Gps);

            string label = $"[Pulsar] {marker.Title} ({marker.OreName})";
            var gps = MyAPIGateway.Session.GPS.Create(label, "Pulsar Ore Survey", marker.Position, true);
            gps.GPSColor = Color.Yellow;
            MyAPIGateway.Session.GPS.AddLocalGps(gps);
            marker.Gps = gps;
        }

        // -----------------------------------------------------------------------
        // STATIC HELPERS (used by InputHandlerService laser scan)
        // -----------------------------------------------------------------------

        /// <summary>
        /// Samples voxel material at multiple penetration depths beyond a surface hit point.
        /// Used by the laser rangefinder for on-demand single-point ore identification.
        /// Returns first non-Stone ore, or null if only stone/empty found.
        /// </summary>
        public static string SampleOreAtDepths(
            IMyVoxelBase voxel, Vector3D surfacePos, Vector3D direction, float[] depths)
        {
            if (voxel?.Storage == null || depths == null || depths.Length == 0) return null;

            var storage = (VRage.Game.Voxels.IMyStorage)voxel.Storage;
            var cache   = new MyStorageData();

            foreach (float depth in depths)
            {
                Vector3D worldSample = surfacePos + direction * depth;
                Vector3D localPos    = worldSample - voxel.PositionLeftBottomCorner;
                Vector3I voxelCell   = Vector3I.Round(localPos);

                if (voxelCell.X < 0 || voxelCell.Y < 0 || voxelCell.Z < 0) continue;

                try
                {
                    cache.Resize(voxelCell, voxelCell);
                    storage.ReadRange(cache, MyStorageDataTypeFlags.Material, 0, voxelCell, voxelCell);
                    byte matIdx = cache.Material(0);
                    if (matIdx == byte.MaxValue) continue;

                    var matDef = MyDefinitionManager.Static.GetVoxelMaterialDefinition(matIdx);
                    if (matDef == null) continue;

                    string ore = matDef.MinedOre;
                    if (!ore.Equals("Stone", StringComparison.OrdinalIgnoreCase))
                        return ore;
                }
                catch { /* unloaded chunk — try next depth */ }
            }

            return null;
        }

        // -----------------------------------------------------------------------
        // PRIVATE HELPERS
        // -----------------------------------------------------------------------

        /// <summary>Builds a comma-separated fauna string from a planet's day+night spawn tables.</summary>
        public static string BuildFaunaString(MyPlanet planet)
        {
            var sb  = new System.Text.StringBuilder();
            var day   = planet.Generator.AnimalSpawnInfo;
            var night = planet.Generator.NightAnimalSpawnInfo;

            Action<MyPlanetAnimalSpawnInfo> add = (info) =>
            {
                if (info?.Animals == null) return;
                foreach (var a in info.Animals)
                {
                    if (!sb.ToString().Contains(a.AnimalType))
                    {
                        if (sb.Length > 0) sb.Append(", ");
                        sb.Append(a.AnimalType);
                    }
                }
            };

            add(day);
            add(night);
            return sb.Length > 0 ? sb.ToString() : "None";
        }
    }
}
