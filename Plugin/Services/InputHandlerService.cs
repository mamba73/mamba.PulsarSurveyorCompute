// Plugin/Services/InputHandlerService.cs
using System;
using System.Text;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Input;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using Plugin.Models;

namespace Plugin.Services
{
    public class InputHandlerService
    {
        private readonly Config                  _config;
        private readonly ConfigService           _configService;
        private readonly PhysicsService          _physics;
        private readonly GpsManagerService       _gpsManager;
        private readonly AsteroidFullScanService _fullScanner;

        // Config dialog state — prevents re-opening while open
        private bool _configDialogOpen = false;

        public InputHandlerService(
            Config config,
            ConfigService configService,
            PhysicsService physics,
            GpsManagerService gpsManager,
            AsteroidFullScanService fullScanner)
        {
            _config        = config;
            _configService = configService;
            _physics       = physics;
            _gpsManager    = gpsManager;
            _fullScanner   = fullScanner;
        }

        /// <summary>
        /// Keyboard shortcuts (called every tick):
        ///
        ///   [T]        → Rangefinder / target info:
        ///                  Asteroid → fires full async scan (all ores, GPS at center)
        ///                  Grid     → shows name, owner, size, distance
        ///                  Planet   → shows name, gravity, atmosphere, distance
        ///                  Note: raycasts are limited by Config.LaserMaxRange (default 50km).
        ///                  Planets further than 50km cannot be hit by the laser.
        ///                  Use "Scan All Planets" in the Ore Detector terminal for distant planets.
        ///
        ///   [Shift+T]  → Clear all GPS markers and reset survey session
        ///
        ///   [Y]        → Force full scan on currently aimed asteroid (same as T on asteroid,
        ///                  useful if T is blocked by another entity in the way)
        /// </summary>
        public void Update(IMyShipController ship, ref double range)
        {
            // --- Config dialog: Ctrl+Alt+/ ---
            if (MyAPIGateway.Input.IsNewKeyPressed(MyKeys.OemQuestion)
                && MyAPIGateway.Input.IsAnyCtrlKeyPressed()
                && MyAPIGateway.Input.IsAnyAltKeyPressed())
            {
                OpenConfigDialog();
                return;
            }

            if (MyAPIGateway.Input.IsNewKeyPressed(MyKeys.T))
            {
                if (MyAPIGateway.Input.IsAnyShiftKeyPressed())
                {
                    _gpsManager.ClearAllMarkers();
                    _fullScanner.ClearScannedCache(); // allow re-scanning previously scanned asteroids
                }
                else
                    PerformRangefinderScan(ship, out range);
                return;
            }

        }

        // -----------------------------------------------------------------------
        // T KEY — RANGEFINDER
        // -----------------------------------------------------------------------

        /// <summary>
        /// Fires a raycast along the ship's forward vector.
        ///
        /// Hit dispatch:
        ///   Planet   → shows planet info (name, surface gravity, atmosphere, distance)
        ///              RANGE NOTE: LaserMaxRange = 50km. Planets are typically 1000–6000km
        ///              away, so the laser will almost never reach them.
        ///              → Use "Scan All Planets" button in Ore Detector terminal instead.
        ///
        ///   Asteroid → triggers AsteroidFullScanService.TryScan() — full async scan,
        ///              shows Scanning...% progress, places GPS at asteroid center with
        ///              all ore types combined. Skips if asteroid already scanned (cached).
        ///
        ///   Grid     → shows grid name, owner, faction, large/small, distance.
        ///              Also creates a GPS marker at the hit point.
        ///
        ///   Miss     → "No target in range" notification.
        /// </summary>
        private void PerformRangefinderScan(IMyShipController ship, out double range)
        {
            range = -1;

            Vector3D start = ship.WorldMatrix.Translation + ship.WorldMatrix.Forward * 5.0;
            Vector3D end   = start + ship.WorldMatrix.Forward * _config.LaserMaxRange;

            // Raycast — then skip hits on own grid + connected subgrids
            var ownIds = Plugin.Services.PhysicsService.GetConnectedGridIds(ship.CubeGrid);

            IHitInfo hit;
            bool gotHit = MyAPIGateway.Physics.CastRay(start, end, out hit);

            // Re-cast up to 3 times to skip own-grid hits (e.g. cockpit nose)
            int attempts = 0;
            while (gotHit && attempts++ < 3)
            {
                var skipGrid = hit.HitEntity?.GetTopMostParent() as IMyCubeGrid;
                if (skipGrid != null && ownIds.Contains(skipGrid.EntityId))
                {
                    // Move start past this hit and cast again
                    Vector3D newStart = hit.Position + ship.WorldMatrix.Forward * 0.5;
                    gotHit = MyAPIGateway.Physics.CastRay(newStart, end, out hit);
                }
                else break;
            }

            if (!gotHit)
            {
                MyAPIGateway.Utilities.ShowNotification(
                    "[PSC] No target in range.  (Max: " + (_config.LaserMaxRange / 1000.0).ToString("F0") + " km)",
                    2500, "White");
                return;
            }

            range = Vector3D.Distance(start, hit.Position);

            // --- PLANET ---
            MyPlanet planet = ResolvePlanet(hit, hit.Position);
            if (planet != null)
            {
                string name    = planet.Generator.Id.SubtypeName;
                float  grav    = planet.Generator.SurfaceGravity;
                bool   hasAtm  = planet.HasAtmosphere;
                float  oxygen  = hasAtm ? planet.Generator.Atmosphere.OxygenDensity : 0f;
                float  radKm   = planet.AverageRadius / 1000f;

                string atm  = hasAtm ? $"Atm O2:{oxygen:F2}" : "No atm";
                string info = $"[PSC] Planet: {name}  {range / 1000.0:F1}km  G:{grav:F2}  R:{radKm:F0}km  {atm}";

                MyAPIGateway.Utilities.ShowNotification(info, 5000, "LightBlue");

                // Also create a GPS at the planet center so it appears in the list
                Vector3D center = planet.PositionLeftBottomCorner + (Vector3D)(planet.SizeInMetres * 0.5f);
                float wellKm    = planet.AverageRadius * 2f / 1000f;
                _gpsManager.CreatePlanetGps(name, center, radKm, grav, wellKm,
                    hasAtm, oxygen, GpsManagerService.BuildFaunaString(planet));
                return;
            }

            IMyEntity hitEnt = hit.HitEntity?.GetTopMostParent();

            // --- ASTEROID → full async scan ---
            if (hitEnt is IMyVoxelBase)
            {
                // TryScan handles the "already scanned" cache check internally
                _fullScanner.TryScan(ship);
                return;
            }

            // --- GRID ---
            if (hitEnt is IMyCubeGrid grid)
            {
                string size    = grid.GridSizeEnum == MyCubeSize.Large ? "Large" : "Small";
                string owner   = ResolveOwnerName(grid);
                string faction = ResolveOwnerFaction(grid);

                string info = $"[PSC] {size} Grid: \"{grid.DisplayName}\"  Owner: {owner}";
                if (!string.IsNullOrEmpty(faction)) info += $"  [{faction}]";
                info += $"  {range:N0}m";

                MyAPIGateway.Utilities.ShowNotification(info, 5000, "White");
                _gpsManager.CreateGridGps(grid.DisplayName, hit.Position, "Detected", size, grid.EntityId);
                return;
            }

            // Unknown entity type
            MyAPIGateway.Utilities.ShowNotification(
                $"[PSC] Hit: {hitEnt?.GetType().Name ?? "unknown"}  {range:N0}m", 2000, "White");
        }

        // -----------------------------------------------------------------------
        // GRID OWNER RESOLUTION
        // -----------------------------------------------------------------------

        /// <summary>
        /// Resolves the display name of the grid's biggest owner (most blocks owned).
        /// Returns "No owner" if unclaimed, "Multiple" if evenly shared.
        /// </summary>
        private static string ResolveOwnerName(IMyCubeGrid grid)
        {
            long ownerId = grid.BigOwners?.Count > 0 ? grid.BigOwners[0] : 0;
            if (ownerId == 0) return "No owner";

            // Try to get identity name
            try
            {
                var player = MyAPIGateway.Players.TryGetSteamId(ownerId);
                if (player != 0)
                {
                    var playerList = new System.Collections.Generic.List<IMyPlayer>();
                    MyAPIGateway.Players.GetPlayers(playerList);
                    foreach (var p in playerList)
                        if (p.IdentityId == ownerId)
                            return p.DisplayName;
                }
            }
            catch { }

            // Fallback: NPC or offline player — show ID
            return $"ID:{ownerId}";
        }

        /// <summary>
        /// Returns the tag of the faction that owns this grid, or empty string if none.
        /// </summary>
        private static string ResolveOwnerFaction(IMyCubeGrid grid)
        {
            long ownerId = grid.BigOwners?.Count > 0 ? grid.BigOwners[0] : 0;
            if (ownerId == 0) return "";
            try
            {
                var faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(ownerId);
                return faction?.Tag ?? "";
            }
            catch { return ""; }
        }

        // -----------------------------------------------------------------------
        // PLANET RESOLUTION
        // -----------------------------------------------------------------------

        private static MyPlanet ResolvePlanet(IHitInfo hit, Vector3D hitPos)
        {
            var direct = hit.HitEntity as MyPlanet;
            if (direct != null) return direct;

            var parent = hit.HitEntity?.GetTopMostParent() as MyPlanet;
            if (parent != null) return parent;

            var nearest = MyGamePruningStructure.GetClosestPlanet(hitPos);
            if (nearest == null) return null;

            double distToCenter = Vector3D.Distance(hitPos, nearest.PositionComp.GetPosition());
            return distToCenter <= nearest.AverageRadius * 1.5 ? nearest : null;
        }

        // -----------------------------------------------------------------------
        // KEY PARSING
        // -----------------------------------------------------------------------

        /// <summary>
        /// Shows config file path and current key bindings via SE's mission screen.
        /// WinForms is not available in Pulsar's Roslyn context.
        /// Edit config.xml in the shown path to change settings — reload world to apply.
        ///
        /// SE ShowMissionScreen signature:
        ///   ShowMissionScreen(string screenTitle, string currentObjectivePrefix,
        ///                     string currentObjective, string description,
        ///                     Action<ResultEnum> callback, string okButtonCaption)
        /// </summary>
        private void OpenConfigDialog()
        {
            if (_configDialogOpen) return;
            _configDialogOpen = true;

            try
            {
                string configPath = System.IO.Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                    "SpaceEngineers", "Storage", "PulsarSurveyorCompute", "config.xml");

                string msg =
                    $"Scan Range:        {_config.PulsarScanRange:N0} m\n" +
                    $"Max Scan Range:    {_config.MaxScanRange:N0} m\n" +
                    $"Laser Max Range:   {_config.LaserMaxRange:N0} m\n" +
                    $"Voxel Stride:      {_config.VoxelScanStride}\n" +
                    $"Planet Refresh:    {(_config.PlanetRefreshEnabled ? "ON" : "OFF")} ({_config.PlanetRefreshTicks} ticks)\n" +
                    $"Tunnel Spacing:    {_config.TunnelRingSpacing:N0} m\n" +
                    $"\nTo change settings, edit:\n{configPath}\n\n" +
                    $"Keys:\n" +
                    $"  [T]        — Rangefinder / full asteroid scan\n" +
                    $"  [Shift+T]  — Clear all GPS markers\n" +
                    $"  [Ctrl+Alt+/] — This screen";

                MyAPIGateway.Utilities.ShowMissionScreen(
                    "Surveyor Compute Config",
                    "", "", msg,
                    r => { _configDialogOpen = false; },
                    "Close");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[Pulsar] ConfigDialog error: {ex.Message}");
                _configDialogOpen = false;
            }
        }

    }
}
