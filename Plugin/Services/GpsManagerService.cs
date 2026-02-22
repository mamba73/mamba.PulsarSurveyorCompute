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
        // Key: voxel.EntityId — one entry per physical asteroid body.
        // This guarantees "one asteroid = one GPS", regardless of how many scan
        // rays or ticks hit the same rock.
        // -----------------------------------------------------------------------
        private readonly Dictionary<long, ResourceMarker> _asteroidCache = new Dictionary<long, ResourceMarker>();

        private int _scanDelay = 0;
        private int _asteroidCounter = 0;

        /// <summary>
        /// Sector label prefix shown in all GPS entries this session.
        /// Editable live via the Terminal textbox control.
        /// Example: "S01" → "[Pulsar] S01 A01 (Iron, Gold)"
        /// </summary>
        public string CurrentSectorName = "S01";

        // 26-direction scan sphere: 6 face normals + 12 edge midpoints + 8 corner diagonals.
        // Used for both manual sector scans and auto-scans to ensure full spherical coverage.
        private static readonly Vector3D[] ScanDirections;

        static GpsManagerService()
        {
            var dirs = new List<Vector3D>();
            for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
            for (int z = -1; z <= 1; z++)
            {
                if (x == 0 && y == 0 && z == 0) continue;
                dirs.Add(Vector3D.Normalize(new Vector3D(x, y, z)));
            }
            ScanDirections = dirs.ToArray(); // 26 directions
        }

        public GpsManagerService(Config config) => _config = config;

        // -----------------------------------------------------------------------
        // PUBLIC API
        // -----------------------------------------------------------------------

        /// <summary>
        /// Auto-scan called every game tick. Internally throttled to ~2 seconds.
        /// Iterates all working Ore Detector blocks on the ship's grid.
        /// Planets and Stone are excluded from results.
        /// </summary>
        public void ScanForVoxels(IMyShipController ship)
        {
            if (_scanDelay++ < 120) return; // ~2 seconds at 60 TPS
            _scanDelay = 0;

            var blocks = new List<IMyTerminalBlock>();
            MyAPIGateway.TerminalActionsHelper
                .GetTerminalSystemForGrid(ship.CubeGrid)
                .GetBlocksOfType<IMyOreDetector>(blocks);

            foreach (var block in blocks)
            {
                var detector = block as IMyOreDetector;
                if (detector != null && detector.IsWorking)
                {
                    ExecuteSphereVoxelScan(detector);
                    ExecuteGridScan(detector);
                }
            }
        }

        /// <summary>
        /// Immediate full scan bypassing the 2-second throttle.
        /// Called by the Terminal "Pulsar: Scan Sector" button and toolbar action.
        /// </summary>
        public void ForceSectorScan(IMyTerminalBlock detectorBlock)
        {
            var detector = detectorBlock as IMyOreDetector;
            if (detector == null) return;

            int before = _asteroidCache.Count;
            ExecuteSphereVoxelScan(detector);
            ExecuteGridScan(detector);
            int found = _asteroidCache.Count - before;

            MyAPIGateway.Utilities.ShowNotification(
                $"[Pulsar] Scan complete. {found} new deposits found ({_asteroidCache.Count} total).",
                4000, MyFontEnum.Green);
        }

        /// <summary>
        /// Clears all Pulsar GPS markers from the player's GPS list and resets the scan session.
        /// Called by Shift+T.
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

        /// <summary>
        /// Records an ore detection on a known voxel body.
        /// - First detection on this asteroid: creates a new GPS marker at the asteroid's center.
        /// - Subsequent detections: appends the ore name to the existing marker label.
        /// - Duplicate ore on same asteroid: silently ignored (no GPS spam).
        /// </summary>
        public void ProcessVoxelDetection(IMyVoxelBase voxel, string oreName)
        {
            long entityId = voxel.EntityId;

            if (!_asteroidCache.ContainsKey(entityId))
            {
                _asteroidCounter++;
                string asteroidId = $"{CurrentSectorName} A{_asteroidCounter:D2}";

                // Place the GPS pin at the asteroid's GEOMETRIC CENTER, not at the ship.
                // This tells the pilot where to fly to, not where they were when scanning.
                Vector3D center = voxel.WorldAABB.Center;

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
                // Same asteroid, new ore type — append and refresh GPS label
                _asteroidCache[entityId].OreName += $", {oreName}";
                SyncGps(_asteroidCache[entityId]);
            }
            // Same ore already listed on this asteroid → no action, no duplicate GPS
        }

        /// <summary>
        /// Creates a GPS marker for a scanned planet.
        /// Format: #Name (R:Xk) (G:X.X) (GW:Xk) [(F+)]
        /// Description contains: fauna list, atmosphere, oxygen level.
        /// </summary>
        public void CreatePlanetGps(
            string name, Vector3D pos, float radiusKm, float gravity,
            float gravityWellKm, bool hasAtmosphere, float oxygenLevel, string faunaInfo)
        {
            // Build label — include (F+) only if fauna is actually present
            bool hasFauna = !string.IsNullOrEmpty(faunaInfo) && faunaInfo != "None";
            string faunaTag = hasFauna ? " (F+)" : "";
            string label = $"#{name} (R:{radiusKm:F0}k) (G:{gravity:F2}) (GW:{gravityWellKm:F0}k){faunaTag}";

            // Build description with all telemetry gathered at scan time
            string atmoLine  = hasAtmosphere ? $"Yes (O2: {oxygenLevel:F2})" : "None";
            string faunaLine = hasFauna ? faunaInfo : "None detected";
            string desc = $"Radius: {radiusKm:F0} km | GW: {gravityWellKm:F0} km\n"
                        + $"Surface Gravity: {gravity:F2} G\n"
                        + $"Atmosphere: {atmoLine}\n"
                        + $"Fauna: {faunaLine}";

            // Remove any existing planet GPS with the same name to avoid duplicates
            var existing = new List<IMyGps>();
            MyAPIGateway.Session.GPS.GetGpsList(MyAPIGateway.Session.Player.IdentityId, existing);
            foreach (var g in existing)
                if (g.Name == label) MyAPIGateway.Session.GPS.RemoveLocalGps(g);

            var gps = MyAPIGateway.Session.GPS.Create(label, desc, pos, true);
            gps.GPSColor = Color.LightBlue;
            MyAPIGateway.Session.GPS.AddLocalGps(gps);
        }

        /// <summary>
        /// Creates a GPS marker for a detected ship or station grid.
        /// Skips if an identical label already exists within 100m (duplicate guard).
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
        // PRIVATE SCAN INTERNALS
        // -----------------------------------------------------------------------

        /// <summary>
        /// Fires rays in all 26 sphere directions from the detector.
        /// Steps outward in SectorSize/4 increments up to the effective range.
        /// For each point that lands inside a non-planet voxel, samples ore material
        /// at multiple penetration depths and records any non-Stone ore found.
        /// </summary>
        private void ExecuteSphereVoxelScan(IMyOreDetector detector)
        {
            // Clamp to config max — prevents detector.Range from bypassing the config limit
            float range = Math.Min(detector.Range, _config.MaxDetectorRange);
            float stepSize = Math.Max(_config.SectorSize / 4f, 5f); // reasonable step granularity
            Vector3D origin = detector.WorldMatrix.Translation;

            foreach (Vector3D dir in ScanDirections)
            {
                for (float d = stepSize; d <= range; d += stepSize)
                {
                    Vector3D checkPos = origin + dir * d;
                    BoundingSphereD sphere = new BoundingSphereD(checkPos, 1.0f);
                    IMyVoxelBase voxel = MyAPIGateway.Session.VoxelMaps.GetOverlappingWithSphere(ref sphere);

                    if (voxel == null || voxel.Storage == null) continue;

                    // Planets are voxels too — exclude them; we only want asteroids here
                    if (voxel is MyPlanet) continue;

                    // Try to read ore at increasing depths inside the voxel surface
                    string ore = SampleOreAtDepths(voxel, checkPos, dir, _config.VoxelPenetrationDepths);
                    if (ore != null)
                    {
                        ProcessVoxelDetection(voxel, ore);
                        break; // This ray found an ore in this asteroid — move to next direction
                    }
                }
            }
        }

        /// <summary>
        /// Sphere-scans for other ship grids within detector range.
        /// Filters out the scanning ship itself and tiny debris (radius ≤ 2m).
        /// </summary>
        private void ExecuteGridScan(IMyOreDetector detector)
        {
            float range = Math.Min(detector.Range, _config.MaxDetectorRange);
            BoundingSphereD scanSphere = new BoundingSphereD(detector.WorldMatrix.Translation, range);
            List<IMyEntity> entities = MyAPIGateway.Entities.GetEntitiesInSphere(ref scanSphere);

            foreach (var ent in entities)
            {
                var grid = ent as IMyCubeGrid;
                if (grid == null) continue;
                if (grid.EntityId == detector.CubeGrid.EntityId) continue; // own ship
                if (grid.WorldVolume.Radius <= 2.0f) continue;             // loose block / debris

                string size = grid.GridSizeEnum == MyCubeSize.Large ? "Large" : "Small";
                long ownerId = (grid.BigOwners != null && grid.BigOwners.Count > 0) ? grid.BigOwners[0] : 0;
                string relation = ownerId != 0
                    ? MyAPIGateway.Session.Player.GetRelationTo(ownerId).ToString()
                    : "Neutral";

                CreateGridGps(grid.DisplayName, grid.WorldMatrix.Translation, relation, size);
            }
        }

        /// <summary>
        /// Samples voxel material at multiple penetration depths beyond the surface hit point.
        /// Uses ReadRange (the reliable low-level storage API) instead of GetMaterialAt.
        ///
        /// Why multiple depths?
        ///   Asteroid surfaces have a Stone "crust" that can be several meters thick.
        ///   0.5m often still hits Stone. Going to 5–20m reaches actual ore veins.
        ///
        /// Returns the first non-Stone ore name found, or null if only Stone (or empty) exists.
        /// </summary>
        public static string SampleOreAtDepths(IMyVoxelBase voxel, Vector3D surfacePos, Vector3D direction, float[] depths)
        {
            if (voxel?.Storage == null) return null;

            var storage = (VRage.Game.Voxels.IMyStorage)voxel.Storage;

            // Depths from config are stored statically here; caller can override if needed

            foreach (float depth in depths)
            {
                Vector3D worldSample = surfacePos + direction * depth;
                Vector3D localPos    = worldSample - voxel.PositionLeftBottomCorner;
                Vector3I voxelCell   = Vector3I.Round(localPos); // 1 unit = 1 meter = 1 voxel cell

                // Clamp to valid storage bounds to avoid out-of-range reads
                if (voxelCell.X < 0 || voxelCell.Y < 0 || voxelCell.Z < 0) continue;

                try
                {
                    // ReadRange is the reliable SE voxel storage API.
                    // We read a single 1×1×1 cell at LOD 0 (full resolution).
                    var cache = new MyStorageData();
                    cache.Resize(voxelCell, voxelCell);
                    // ReadRange expects MyStorageDataTypeFlags (not Enum) — confirmed from SE source
                    storage.ReadRange(cache, MyStorageDataTypeFlags.Material, 0, voxelCell, voxelCell);

                    // Material(int linearIndex) — for a 1×1×1 cache the only valid index is 0
                    byte matIndex = cache.Material(0);
                    if (matIndex == byte.MaxValue) continue; // empty voxel cell / air

                    var matDef = MyDefinitionManager.Static.GetVoxelMaterialDefinition(matIndex);
                    if (matDef == null) continue;

                    string ore = matDef.MinedOre;

                    // Skip Stone — it covers everything and adds no survey value
                    if (!ore.Equals("Stone", StringComparison.OrdinalIgnoreCase))
                        return ore; // Non-stone material found at this depth
                }
                catch
                {
                    // Voxel storage may throw on out-of-bounds or unloaded chunks
                    // Silently skip this depth and try the next
                }
            }

            return null; // All sampled depths returned Stone or empty
        }

        /// <summary>
        /// Removes the old GPS entry for this marker and recreates it with the updated ore list.
        /// Label format: [Pulsar] S01 A01 (Iron, Gold, Uranium)
        /// </summary>
        private void SyncGps(ResourceMarker marker)
        {
            if (marker.Gps != null)
                MyAPIGateway.Session.GPS.RemoveLocalGps(marker.Gps);

            string label = $"[Pulsar] {marker.Title} ({marker.OreName})";
            var gps = MyAPIGateway.Session.GPS.Create(label, "Pulsar Ore Survey", marker.Position, true);
            gps.GPSColor = Color.Yellow;
            MyAPIGateway.Session.GPS.AddLocalGps(gps);
            marker.Gps = gps; // Save reference for next update
        }
    }
}
