// Plugin/Services/TerminalControlService.cs
using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace Plugin.Services
{
    public class TerminalControlService
    {
        private readonly GpsManagerService _gpsManager;
        private readonly ConfigService     _configService;

        // STATIC: AddAction<T> is permanent for the entire process lifetime.
        // Calling it twice (on world reload) creates duplicates that break both entries.
        private static bool _actionsAddedToEngine = false;

        // INSTANCE: CustomControlGetter is session-scoped — re-register each session.
        private bool _hookRegistered = false;

        public TerminalControlService(GpsManagerService gpsManager, ConfigService configService)
        {
            _gpsManager    = gpsManager;
            _configService = configService;
        }

        /// <summary>
        /// Called from MainPlugin.Update() on every tick until registration succeeds.
        ///
        /// WHY NOT FROM Init():
        ///   IPlugin.Init() fires before any session or world exists.
        ///   MyAPIGateway.Utilities / TerminalControls are session-bound and throw
        ///   NullReferenceException if called from Init().
        ///   Update() only runs once a session is active — all APIs are safe.
        ///
        /// TOOLBAR ACTION TIMING:
        ///   AddAction must fire before any terminal panel of that block type is opened.
        ///   SE caches the action list per block type on first open.
        ///   InvokeOnGameThread defers the call to the main sim thread at the earliest
        ///   opportunity, before any player interaction is possible.
        ///
        /// STATIC FLAG:
        ///   _actionsAddedToEngine prevents duplicate AddAction calls across world reloads.
        /// </summary>
        public void Initialize()
        {
            if (_hookRegistered) return;
            if (MyAPIGateway.TerminalControls == null) return;
            if (MyAPIGateway.Utilities == null) return;

            try
            {
                if (MyAPIGateway.Utilities.IsDedicated)
                {
                    _hookRegistered = true;
                    return;
                }

                MyAPIGateway.TerminalControls.CustomControlGetter += AddOreDetectorControls;
                _hookRegistered = true;

                if (!_actionsAddedToEngine)
                {
                    MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                    {
                        try
                        {
                            RegisterToolbarActions();
                            _actionsAddedToEngine = true;

                            string ver = _configService?.Data?.PluginVersion ?? "?";
                            MyAPIGateway.Utilities.ShowNotification(
                                $"[Pulsar Surveyor Compute v{ver}] Loaded.",
                                5000, VRage.Game.MyFontEnum.Green);
                        }
                        catch (Exception ex)
                        {
                            MyLog.Default.WriteLineAndConsole(
                                $"[Pulsar] RegisterToolbarActions error: {ex.Message}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[Pulsar] Initialize error: {ex.Message}");
            }
        }

        /// <summary>
        /// Registers G-menu / hotbar toolbar actions for the Ore Detector block type.
        /// Must fire before the first Ore Detector terminal panel is opened.
        /// </summary>
        private void RegisterToolbarActions()
        {
            var scanAction = MyAPIGateway.TerminalControls.CreateAction<IMyOreDetector>("PulsarScanSectorAction");
            scanAction.Name           = new StringBuilder("Pulsar: Scan Sector");
            scanAction.Icon           = @"Textures\GUI\Icons\Actions\Start.dds";
            scanAction.Action         = (b) => _gpsManager.ForceSectorScan(b);
            scanAction.Writer         = (b, sb) => sb.Append("Pulsar\nScan");
            scanAction.ValidForGroups = false;
            MyAPIGateway.TerminalControls.AddAction<IMyOreDetector>(scanAction);

            var planetAction = MyAPIGateway.TerminalControls.CreateAction<IMyOreDetector>("PulsarScanPlanetsAction");
            planetAction.Name           = new StringBuilder("Pulsar: Scan All Planets");
            planetAction.Icon           = @"Textures\GUI\Icons\Actions\Start.dds";
            planetAction.Action         = (b) => _gpsManager.ScanAllPlanets();
            planetAction.Writer         = (b, sb) => sb.Append("Pulsar\nPlanets");
            planetAction.ValidForGroups = false;
            MyAPIGateway.TerminalControls.AddAction<IMyOreDetector>(planetAction);

            var clearAction = MyAPIGateway.TerminalControls.CreateAction<IMyOreDetector>("PulsarClearMarkersAction");
            clearAction.Name           = new StringBuilder("Pulsar: Clear Markers");
            clearAction.Icon           = @"Textures\GUI\Icons\Actions\Reset.dds";
            clearAction.Action         = (b) => _gpsManager.ClearAllMarkers();
            clearAction.Writer         = (b, sb) => sb.Append("Pulsar\nClear");
            clearAction.ValidForGroups = false;
            MyAPIGateway.TerminalControls.AddAction<IMyOreDetector>(clearAction);
        }

        /// <summary>
        /// Injects Pulsar controls into the Ore Detector terminal panel.
        /// Called by SE every time an Ore Detector panel is opened.
        ///
        /// Controls:
        ///   0. Version label  — confirms plugin loaded and shows version from config
        ///   1. Scan Sector    — LOD2 voxel scan for ore within PulsarScanRange
        ///   2. Scan Planets   — iterates all game entities for MyPlanet
        ///   3. Scan Range     — Pulsar's own range slider (bypasses ~150m vanilla block cap)
        ///   4. Sector Name    — GPS label prefix
        ///   5. Clear Markers  — resets survey session
        /// </summary>
        private void AddOreDetectorControls(IMyTerminalBlock block, List<IMyTerminalControl> controls)
        {
            if (!(block is IMyOreDetector)) return;
            if (controls.Exists(x => x.Id == "PulsarVersionLabel")) return;

            string ver = _configService?.Data?.PluginVersion ?? "?";

            var versionLabel = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlLabel, IMyOreDetector>("PulsarVersionLabel");
            versionLabel.Label              = MyStringId.GetOrCompute($"Surveyor Compute v{ver}");
            versionLabel.SupportsMultipleBlocks = false;
            controls.Add(versionLabel);

            var scanBtn = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyOreDetector>("PulsarScanSector");
            scanBtn.Title  = MyStringId.GetOrCompute("Pulsar: Scan Sector for Ore");
            scanBtn.Action = (b) => _gpsManager.ForceSectorScan(b);
            controls.Add(scanBtn);

            var planetBtn = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyOreDetector>("PulsarScanPlanets");
            planetBtn.Title  = MyStringId.GetOrCompute("Pulsar: Scan All Planets");
            planetBtn.Action = (b) => _gpsManager.ScanAllPlanets();
            controls.Add(planetBtn);

            var rangeSlider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyOreDetector>("PulsarScanRange");
            rangeSlider.Title        = MyStringId.GetOrCompute("Scan Range");
            // Min 100m, max from config. DefaultValue sets the slider thumb position on first open.
            rangeSlider.SetLimits(100f, _configService.Data.MaxScanRange);
            rangeSlider.Getter       = (b) => _gpsManager.PulsarScanRange;
            rangeSlider.Setter       = (b, v) =>
            {
                _gpsManager.PulsarScanRange         = v;
                _configService.Data.PulsarScanRange = v;
            };
            rangeSlider.Writer       = (b, sb) => sb.AppendFormat("{0:N0} m", _gpsManager.PulsarScanRange);
            controls.Add(rangeSlider);

            var sectorBox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlTextbox, IMyOreDetector>("PulsarSectorName");
            sectorBox.Title  = MyStringId.GetOrCompute("Pulsar: Sector Name");
            sectorBox.Getter = (b) => new StringBuilder(_gpsManager.CurrentSectorName);
            sectorBox.Setter = (b, v) => _gpsManager.CurrentSectorName = v.ToString();
            controls.Add(sectorBox);

            var clearBtn = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyOreDetector>("PulsarClearMarkers");
            clearBtn.Title  = MyStringId.GetOrCompute("Pulsar: Clear All Markers");
            clearBtn.Action = (b) => _gpsManager.ClearAllMarkers();
            controls.Add(clearBtn);
        }

        public void Terminate()
        {
            if (_hookRegistered && MyAPIGateway.TerminalControls != null)
            {
                MyAPIGateway.TerminalControls.CustomControlGetter -= AddOreDetectorControls;
                _hookRegistered = false;
            }
            // _actionsAddedToEngine stays true — AddAction cannot be undone.
        }
    }
}
