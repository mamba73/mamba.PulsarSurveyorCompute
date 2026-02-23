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

        private readonly Dictionary<long, ResourceMarker> _asteroidCache
            = new Dictionary<long, ResourceMarker>();

        private int _asteroidCounter = 0;

        public string CurrentSectorName = "S01";

        /// <summary>
        /// Pulsar's independent scan radius — NOT the vanilla Ore Detector block range.
        /// Updated live by the terminal slider.
        /// </summary>
        public float PulsarScanRange;

        public GpsManagerService(Config config)
        {
            _config         = config;
            PulsarScanRange = config.PulsarScanRange;
        }

        // -----------------------------------------------------------------------
        // PUBLIC API
        // -----------------------------------------------------------------------

        /// <summary>
        /// Manual sector scan. Scans all asteroid voxel maps within PulsarScanRange.
        /// Grids excluded — detected only via laser (T key).
        /// </summary>
        public void ForceSectorScan(IMyTerminalBlock detectorBlock)
        {
            int before = _asteroidCache.Count;
            ExecuteAsteroidScan(detectorBlock.WorldMatrix.Translation);
            int found = _asteroidCache.Count - before;

            MyAPIGateway.Utilities.ShowNotification(
                $"[Pulsar] Scan complete — {found} new deposits ({_asteroidCache.Count} total, {PulsarScanRange:N0}m range).",
                4000, MyFontEnum.Green);
        }

        /// <summary>Iterates ALL game entities to find and GPS-mark every planet.</summary>
        public void ScanAllPlanets()
        {
            int found = 0;
            var entities = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(entities);

            foreach (var ent in entities)
            {
                var planet = ent as MyPlanet;
                if (planet == null) continue;

                string   name          = planet.Generator.Id.SubtypeName;
                Vector3D center        = planet.PositionLeftBottomCorner + (Vector3D)(planet.SizeInMetres * 0.5f);
                float    radiusKm      = planet.AverageRadius / 1000f;
                float    gravity       = planet.Generator.SurfaceGravity;
                float    gravityWellKm = (planet.AverageRadius * 2f) / 1000f;
                bool     hasAtm        = planet.HasAtmosphere;
                float    oxygen        = hasAtm ? planet.Generator.Atmosphere.OxygenDensity : 0f;
                string   fauna         = BuildFaunaString(planet);

                CreatePlanetGps(name, center, radiusKm, gravity, gravityWellKm, hasAtm, oxygen, fauna);
                found++;
            }

            MyAPIGateway.Utilities.ShowNotification(
                $"[Pulsar] Planet scan complete — {found} planet(s) mapped.", 4000, MyFontEnum.Green);
        }

        /// <summary>Clears all Pulsar GPS markers and resets scan session (Shift+T).</summary>
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
        /// Records an ore detection. One GPS per entity, at the asteroid's geometric center.
        /// Additional ores on same entity → appended to existing GPS label.
        /// </summary>
        public void ProcessVoxelDetection(IMyVoxelBase voxel, string oreName)
        {
            long entityId = voxel.EntityId;

            if (!_asteroidCache.ContainsKey(entityId))
            {
                _asteroidCounter++;
                var marker = new ResourceMarker
                {
                    EntityId = entityId,
                    Position = voxel.WorldAABB.Center,
                    OreName  = oreName,
                    Title    = $"{CurrentSectorName} A{_asteroidCounter:D2}"
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

        public void CreatePlanetGps(
            string name, Vector3D pos, float radiusKm, float gravity,
            float gravityWellKm, bool hasAtmosphere, float oxygenLevel, string faunaInfo)
        {
            bool   hasFauna = !string.IsNullOrEmpty(faunaInfo) && faunaInfo != "None";
            string label    = $"#{name} (R:{radiusKm:F0}k) (G:{gravity:F2}) (GW:{gravityWellKm:F0}k){(hasFauna ? " (F+)" : "")}";
            string desc     = $"Radius: {radiusKm:F0} km | GW: {gravityWellKm:F0} km\n"
                            + $"Surface Gravity: {gravity:F2} G\n"
                            + $"Atmosphere: {(hasAtmosphere ? $"Yes (O2:{oxygenLevel:F2})" : "None")}\n"
                            + $"Fauna: {(hasFauna ? faunaInfo : "None detected")}";

            var existing = new List<IMyGps>();
            MyAPIGateway.Session.GPS.GetGpsList(MyAPIGateway.Session.Player.IdentityId, existing);
            foreach (var g in existing)
                if (g.Name == label) MyAPIGateway.Session.GPS.RemoveLocalGps(g);

            var gps = MyAPIGateway.Session.GPS.Create(label, desc, pos, true);
            gps.GPSColor = Color.LightBlue;
            MyAPIGateway.Session.GPS.AddLocalGps(gps);
        }

        /// <summary>
        /// Creates GPS for a grid detected by laser. Skips own ship by entityId.
        /// </summary>
        public void CreateGridGps(string name, Vector3D pos, string relation, string size, long gridEntityId)
        {
            var player     = MyAPIGateway.Session.Player;
            var controlled = player?.Controller?.ControlledEntity?.Entity?.GetTopMostParent() as IMyCubeGrid;
            if (controlled != null && controlled.EntityId == gridEntityId) return;

            var controlledSc = player?.Controller?.ControlledEntity as IMyShipController;
            if (controlledSc != null && controlledSc.CubeGrid.EntityId == gridEntityId) return;

            string label = $"[Grid] {name} ({size})";
            var existing = new List<IMyGps>();
            MyAPIGateway.Session.GPS.GetGpsList(player?.IdentityId ?? 0, existing);
            foreach (var g in existing)
                if (g.Name == label && Vector3D.Distance(g.Coords, pos) < 100) return;

            var gps = MyAPIGateway.Session.GPS.Create(label, $"Relation: {relation}", pos, true);
            gps.GPSColor = (relation == "Enemies" || relation == "Hostile") ? Color.Red : Color.White;
            MyAPIGateway.Session.GPS.AddLocalGps(gps);
        }

        // -----------------------------------------------------------------------
        // SCAN ENGINE
        // -----------------------------------------------------------------------

        /// <summary>
        /// Entity-based asteroid discovery within PulsarScanRange.
        /// IMyVoxelMap = asteroids only (MyPlanet does NOT implement IMyVoxelMap).
        /// </summary>
        private void ExecuteAsteroidScan(Vector3D origin)
        {
            var sphere   = new BoundingSphereD(origin, PulsarScanRange);
            var entities = MyAPIGateway.Entities.GetEntitiesInSphere(ref sphere);

            int scanned = 0;
            foreach (var ent in entities)
            {
                var voxelMap = ent as IMyVoxelMap;
                if (voxelMap == null) continue;
                ScanVoxelStorage(voxelMap);
                scanned++;
            }
        }

        /// <summary>
        /// Scans a single asteroid's voxel storage for ore using LOD-based reading.
        ///
        /// WHY PREVIOUS VERSION ONLY FOUND ICE:
        ///   Stride=8 on LOD0 means each sample point is 8m apart.
        ///   SE ore veins (Iron, Nickel, Silicon) can be as narrow as 3-5 voxels (3-5m).
        ///   An 8m stride jumps OVER these narrow veins entirely.
        ///   Ice asteroids are entirely composed of Ice → every sample hits Ice → always detected.
        ///   Iron/Nickel/etc asteroids have stone shell + narrow veins → missed by 8m stride.
        ///
        /// FIX: Read storage at LOD2 (Level of Detail 2).
        ///   At LOD2, each "cell" in the storage represents 4×4×4 LOD0 voxels = 4m³ chunk.
        ///   The storage automatically downsamples and aggregates content/material.
        ///   A 4m ore vein will occupy roughly 1 cell at LOD2 → reliably detected.
        ///   Iteration count = (storageSize/4)³ instead of storageSize³ → 64× fewer reads.
        ///
        /// LOD2 STRIDE:
        ///   storageSize at LOD2 = LOD0_size >> 2 (right-shift by LOD level).
        ///   We iterate in units of 1 cell at LOD2 (which = 4m in world space).
        ///   Config.VoxelScanStride now acts as a multiplier on top of LOD2 cells.
        ///   Default stride=1 at LOD2 = thorough scan. stride=2 = fast but may miss 4m veins.
        /// </summary>
        private void ScanVoxelStorage(IMyVoxelMap voxelMap)
        {
            if (voxelMap.Storage == null) return;

            var     storage = (VRage.Game.Voxels.IMyStorage)voxelMap.Storage;
            int     lod     = 2; // LOD2: each cell = 4m³ world-space cube
            Vector3I lod0Size = storage.Size;

            // LOD2 storage dimensions = LOD0 size >> LOD level
            Vector3I lodSize = new Vector3I(
                Math.Max(1, lod0Size.X >> lod),
                Math.Max(1, lod0Size.Y >> lod),
                Math.Max(1, lod0Size.Z >> lod));

            int     stride    = Math.Max(1, _config.VoxelScanStride); // stride in LOD2 cells
            var     cache     = new MyStorageData();
            var     foundOres = new HashSet<string>();

            for (int x = 0; x < lodSize.X; x += stride)
            for (int y = 0; y < lodSize.Y; y += stride)
            for (int z = 0; z < lodSize.Z; z += stride)
            {
                Vector3I cell = new Vector3I(x, y, z);

                try
                {
                    // Pass 1: content — skip empty (air) cells cheaply
                    cache.Resize(cell, cell);
                    storage.ReadRange(cache, MyStorageDataTypeFlags.Content, lod, cell, cell);
                    if (cache.Content(0) < 32) continue; // < ~12% solid at LOD2 = skip

                    // Pass 2: material on solid cells
                    cache.Resize(cell, cell);
                    storage.ReadRange(cache, MyStorageDataTypeFlags.Material, lod, cell, cell);
                    byte matIdx = cache.Material(0);
                    if (matIdx == byte.MaxValue) continue;

                    var matDef = MyDefinitionManager.Static.GetVoxelMaterialDefinition(matIdx);
                    if (matDef == null) continue;

                    string ore = matDef.MinedOre;
                    if (ore.Equals("Stone", StringComparison.OrdinalIgnoreCase)) continue;

                    // foundOres.Add returns false if already in set → no duplicate GPS
                    if (foundOres.Add(ore))
                        ProcessVoxelDetection(voxelMap, ore);
                }
                catch { /* unloaded / corrupted chunk — skip silently */ }
            }
        }

        // -----------------------------------------------------------------------
        // STATIC HELPERS (used by InputHandlerService for laser scan)
        // -----------------------------------------------------------------------

        /// <summary>
        /// Samples voxel material at multiple penetration depths from a surface hit point.
        /// Used by the laser rangefinder for on-demand single-point ore ID.
        /// Returns first non-Stone ore or null.
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
                catch { }
            }
            return null;
        }

        public static string BuildFaunaString(MyPlanet planet)
        {
            var sb    = new System.Text.StringBuilder();
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
    }
}
