// Plugin/Services/AsteroidFullScanService.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRage.Voxels;
using VRageMath;

namespace Plugin.Services
{
    /// <summary>
    /// Full asteroid deep-scan service.
    ///
    /// Triggered by the FullScanKey (default Y).
    /// Performs a complete LOD2 voxel scan of the currently targeted asteroid,
    /// collecting ALL ore types present, then places a single GPS marker at the
    /// asteroid center listing every ore found.
    ///
    /// ASYNC DESIGN:
    ///   The voxel scan runs on a background thread (Task.Run) to avoid freezing
    ///   the game. ReadRange() is read-only and safe to call off-thread.
    ///   All MyAPIGateway calls (ShowNotification, GPS creation) are routed back
    ///   to the main game thread via InvokeOnGameThread().
    ///
    /// PROGRESS REPORTING:
    ///   Total LOD2 cell count is known before the scan starts.
    ///   Progress % = cells processed / total cells.
    ///   A "Scanning... X%" notification is updated every 1000 cells processed.
    ///   The notification stays on screen (lifetime 0) until explicitly replaced.
    ///   On completion, a "Scan complete" notification shows all found ores.
    ///
    /// SCAN LOCK:
    ///   _isScanning flag prevents launching a second scan while one is in progress.
    ///   Only one asteroid can be scanned at a time.
    /// </summary>
    public class AsteroidFullScanService
    {
        private readonly GpsManagerService _gpsManager;
        private readonly ConfigService     _configService;

        // Prevents overlapping scans
        private volatile bool _isScanning = false;

        // Cache of entity IDs that have already been fully scanned this session.
        // Prevents re-scanning the same asteroid on every T keypress.
        // Cleared with Shift+T (ClearScannedCache).
        private readonly System.Collections.Generic.HashSet<long> _scannedIds
            = new System.Collections.Generic.HashSet<long>();

        // Persistent notification handle — reused to avoid notification spam
        private IMyHudNotification _scanNote;

        public AsteroidFullScanService(GpsManagerService gpsManager, ConfigService configService)
        {
            _gpsManager    = gpsManager;
            _configService = configService;
        }

        /// <summary>
        /// Entry point. Raycasts from ship forward vector, resolves the asteroid, starts scan.
        /// Safe to call every tick — guarded by _isScanning flag.
        /// </summary>
        public void TryScan(IMyShipController ship)
        {
            if (_isScanning)
            {
                ShowNote("[PSC] Already scanning — please wait...", MyFontEnum.Red);
                return;
            }

            Vector3D start = ship.WorldMatrix.Translation + ship.WorldMatrix.Forward * 5.0;
            Vector3D end   = start + ship.WorldMatrix.Forward * _configService.Data.LaserMaxRange;

            IHitInfo hit;
            if (!MyAPIGateway.Physics.CastRay(start, end, out hit))
            {
                ShowNote("[PSC] No asteroid in sights.", MyFontEnum.White);
                return;
            }

            // Resolve the hit entity — must be a voxel map (asteroid), not a planet
            var voxelMap = ResolveAsteroid(hit);
            if (voxelMap == null)
            {
                ShowNote("[PSC] Full scan: not an asteroid.", MyFontEnum.White);
                return;
            }

            // Capture storage reference and center before going async
            // (entity references are safe to read from background thread — they're managed objects)
            var storage = (VRage.Game.Voxels.IMyStorage)voxelMap.Storage;
            if (storage == null)
            {
                ShowNote("[PSC] Asteroid storage unavailable.", MyFontEnum.Red);
                return;
            }

            Vector3D asteroidCenter = voxelMap.WorldAABB.Center;
            string   asteroidName   = voxelMap.Name ?? voxelMap.EntityId.ToString();
            long     entityId       = voxelMap.EntityId;

            // Skip if already scanned this session — avoids re-scan spam on repeated T presses
            if (_scannedIds.Contains(entityId))
            {
                ShowNote($"[PSC] {asteroidName} already scanned. Shift+T to reset.", MyFontEnum.White, 3000);
                return;
            }
            int      stride         = Math.Max(1, _configService.Data.VoxelScanStride);

            _isScanning = true;
            ShowNote("[PSC] Scanning... 0%  (stay still)", MyFontEnum.White);

            // ASYNC: run the entire voxel iteration off the main thread
            Task.Run(() => RunScanAsync(storage, asteroidCenter, asteroidName, entityId, stride, voxelMap));
        }

        // -----------------------------------------------------------------------
        // BACKGROUND SCAN (runs on thread pool thread)
        // -----------------------------------------------------------------------

        private void RunScanAsync(
            VRage.Game.Voxels.IMyStorage storage,
            Vector3D asteroidCenter,
            string asteroidName,
            long entityId,
            int stride,
            IMyVoxelBase voxelRef)
        {
            var foundOres = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                // LOD2: each cell = 4m³ aggregate of 4×4×4 LOD0 voxels
                int lod = 2;
                Vector3I lod0Size = storage.Size;
                Vector3I lodSize = new Vector3I(
                    Math.Max(1, lod0Size.X >> lod),
                    Math.Max(1, lod0Size.Y >> lod),
                    Math.Max(1, lod0Size.Z >> lod));

                // Pre-calculate total cells so we can report percentage
                long totalCells   =
                    (long)Math.Ceiling((double)lodSize.X / stride) *
                    (long)Math.Ceiling((double)lodSize.Y / stride) *
                    (long)Math.Ceiling((double)lodSize.Z / stride);
                long processed    = 0;
                int  lastReported = -1;

                var cache = new MyStorageData();

                for (int x = 0; x < lodSize.X; x += stride)
                for (int y = 0; y < lodSize.Y; y += stride)
                for (int z = 0; z < lodSize.Z; z += stride)
                {
                    processed++;

                    // Update progress notification every 0.5%
                    int pct = (int)(processed * 100L / Math.Max(1, totalCells));
                    if (pct != lastReported && pct % 1 == 0) // every 1%
                    {
                        lastReported = pct;
                        int capturedPct = pct;
                        MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                            ShowNote($"[PSC] Scanning... {capturedPct}%  (stay still)", MyFontEnum.White));
                    }

                    Vector3I cell = new Vector3I(x, y, z);
                    try
                    {
                        // Pass 1: content — skip air/empty cells cheaply
                        cache.Resize(cell, cell);
                        storage.ReadRange(cache, MyStorageDataTypeFlags.Content, lod, cell, cell);
                        if (cache.Content(0) < 32) continue;

                        // Pass 2: material on solid cells
                        cache.Resize(cell, cell);
                        storage.ReadRange(cache, MyStorageDataTypeFlags.Material, lod, cell, cell);
                        byte matIdx = cache.Material(0);
                        if (matIdx == byte.MaxValue) continue;

                        var matDef = Sandbox.Definitions.MyDefinitionManager.Static
                            .GetVoxelMaterialDefinition(matIdx);
                        if (matDef == null) continue;

                        string ore = matDef.MinedOre;
                        if (!ore.Equals("Stone", StringComparison.OrdinalIgnoreCase))
                            foundOres.Add(ore);
                    }
                    catch { /* corrupted/unloaded chunk — skip */ }
                }

                // Done — return to main thread to create GPS and show result
                var oreList = new List<string>(foundOres);
                long capturedId = entityId;
MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                    OnScanComplete(voxelRef, asteroidCenter, asteroidName, oreList, capturedId));
            }
            catch (Exception ex)
            {
                MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                {
                    ShowNote($"[PSC] Scan error: {ex.Message}", MyFontEnum.Red);
                    _isScanning = false;
                });
                MyLog.Default.WriteLineAndConsole($"[Pulsar] Full scan error: {ex}");
            }
        }

        // -----------------------------------------------------------------------
        // COMPLETION (back on main thread)
        // -----------------------------------------------------------------------

        private void OnScanComplete(
            IMyVoxelBase voxelRef,
            Vector3D center,
            string name,
            List<string> ores,
            long entityId)
        {
            _isScanning = false;
            _scannedIds.Add(entityId);

            if (ores.Count == 0)
            {
                ShowNote("[PSC] Deep scan: no ore found (all Stone).", MyFontEnum.White);
                return;
            }

            ores.Sort(); // alphabetical for consistent GPS label
            string oreStr = string.Join(", ", ores);

            // Create or update GPS at asteroid center (combines all ores in one marker)
            _gpsManager.CreateDeepScanGps(name, center, oreStr);

            ShowNote($"[PSC] Scan complete! Found: {oreStr}", MyFontEnum.Green, 6000);
        }

        /// <summary>
        /// Clears the already-scanned cache. Called by Shift+T (ClearAllMarkers).
        /// After clearing, all asteroids can be re-scanned.
        /// </summary>
        public void ClearScannedCache() => _scannedIds.Clear();

        // -----------------------------------------------------------------------
        // HELPERS
        // -----------------------------------------------------------------------

        /// <summary>
        /// Resolves the hit entity to an asteroid voxel map.
        /// Returns null if the hit is a planet or a non-voxel entity.
        /// </summary>
        private static IMyVoxelBase ResolveAsteroid(IHitInfo hit)
        {
            var ent = hit.HitEntity?.GetTopMostParent() ?? hit.HitEntity;

            // Must be a voxel map — planets use MyPlanet which is NOT IMyVoxelMap
            var voxelMap = ent as IMyVoxelMap;
            if (voxelMap != null) return voxelMap;

            // Some asteroids hit as IMyVoxelBase but not IMyVoxelMap — accept both
            return ent as IMyVoxelBase;
        }

        /// <summary>
        /// Shows or updates the persistent scan notification.
        /// lifetime = 0 keeps the notification on-screen indefinitely.
        /// Pass a positive lifetime (ms) for auto-hiding final messages.
        /// </summary>
        private void ShowNote(string text, MyFontEnum font, int lifetime = 0)
        {
            if (_scanNote == null)
                _scanNote = MyAPIGateway.Utilities.CreateNotification("", lifetime, font.ToString());

            _scanNote.Font = font.ToString();
            _scanNote.Text = text;
            _scanNote.AliveTime = lifetime > 0 ? lifetime : int.MaxValue;
            _scanNote.ResetAliveTime();
            _scanNote.Show();
        }
    }
}
