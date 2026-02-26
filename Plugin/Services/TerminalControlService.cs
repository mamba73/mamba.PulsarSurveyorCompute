// Plugin/Services/TerminalControlService.cs
using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game.ModAPI;
using VRage.Utils;
using Sandbox.ModAPI.Interfaces;

namespace Plugin.Services
{
    public class TerminalControlService
    {
        private readonly GpsManagerService _gpsManager;
        private readonly ConfigService     _configService;

        // STATIC flag — AddAction<T> is PERMANENT for the entire game process lifetime.
        // If two sessions load in the same process (e.g. player leaves world and joins again),
        // calling AddAction a second time creates duplicate toolbar actions which breaks both.
        // Using static ensures we add exactly once per process, regardless of session count.
        private static bool _actionsAddedToEngine = false;

        // INSTANCE flag — CustomControlGetter hook is session-scoped.
        // Must be re-registered each session (it's cleared when a session unloads).
        private bool _hookRegistered = false;

        // Version shown in terminal label and startup notification
        public const string PLUGIN_VERSION = "1.0.118";

        public TerminalControlService(GpsManagerService gpsManager, ConfigService configService)
        {
            _gpsManager    = gpsManager;
            _configService = configService;
        }

        /// <summary>
        /// Called from MainPlugin.Init() via InvokeOnGameThread.
        ///
        /// TIMING FIX:
        ///   Previously registered from Update() on the first tick.
        ///   Problem: SE caches the toolbar action list for each block type the first time
        ///   any terminal of that type is opened. If a terminal was open BEFORE the first
        ///   Update() tick, AddAction() was too late — actions never appeared.
        ///
        ///   Fix: Call from Init() using MyAPIGateway.Utilities.InvokeOnGameThread().
        ///   InvokeOnGameThread defers execution to the next simulation tick on the
        ///   main game thread, where all terminal APIs are guaranteed ready.
        ///   This fires BEFORE any player interaction (loading screen is still up).
        ///
        /// STATIC FLAG:
        ///   AddAction() is permanent per process. Static _actionsAddedToEngine ensures
        ///   it's called exactly once even if the player reloads a world in the same session.
        ///   CustomControlGetter must still be re-hooked each session (instance flag).
        ///
        /// DEDICATED SERVER:
        ///   IsDedicated check skips UI registration on server processes where no
        ///   terminal panels exist and notifications would cause null-ref exceptions.
        /// </summary>
        public void InitEarly()
        {
            MyAPIGateway.Utilities.InvokeOnGameThread(() =>
            {
                try
                {
                    if (MyAPIGateway.Utilities.IsDedicated) return;
                    if (MyAPIGateway.TerminalControls == null) return;

                    // Hook CustomControlGetter every session (session-scoped, cleared on unload)
                    if (!_hookRegistered)
                    {
                        MyAPIGateway.TerminalControls.CustomControlGetter += AddOreDetectorControls;
                        _hookRegistered = true;
                    }

                    // AddAction is permanent per process — only register once
                    if (!_actionsAddedToEngine)
                    {
                        RegisterToolbarActions();
                        _actionsAddedToEngine = true;
                    }

                    // Show version on first load so player can confirm plugin is running
                    MyAPIGateway.Utilities.ShowNotification(
                        $"[Pulsar Surveyor Compute v{PLUGIN_VERSION}] Loaded.", 5000,
                        VRage.Game.MyFontEnum.Green);
                }
                catch (Exception ex)
                {
                    MyLog.Default.WriteLineAndConsole($"[Pulsar] TerminalControl init error: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Fallback called from Update() in case InvokeOnGameThread fired too early.
        /// Only runs once, only if hook somehow wasn't registered by InitEarly.
        /// </summary>
        public void Initialize()
        {
            if (_hookRegistered || MyAPIGateway.TerminalControls == null) return;
            if (MyAPIGateway.Utilities.IsDedicated) { _hookRegistered = true; return; }

            MyAPIGateway.TerminalControls.CustomControlGetter += AddOreDetectorControls;
            _hookRegistered = true;

            if (!_actionsAddedToEngine)
            {
                RegisterToolbarActions();
                _actionsAddedToEngine = true;
            }
        }

        /// <summary>
        /// Registers G-menu / hotbar toolbar actions for the Ore Detector block type.
        ///
        /// IMPORTANT: Must be called before ANY Ore Detector terminal panel is opened.
        ///   SE caches the action list per block type on first panel open.
        ///   Actions added after that are invisible until the game is restarted.
        ///
        /// This is why AddAction must fire early (from InitEarly/InvokeOnGameThread),
        /// not lazily on first Update() tick.
        /// </summary>
        private void RegisterToolbarActions()
        {
            // Scan Sector
            var scanAction = MyAPIGateway.TerminalControls.CreateAction<IMyOreDetector>("PulsarScanSectorAction");
            scanAction.Name           = new StringBuilder("Pulsar: Scan Sector");
            scanAction.Icon           = @"Textures\GUI\Icons\Actions\Start.dds";
            scanAction.Action         = (b) => _gpsManager.ForceSectorScan(b);
            scanAction.Writer         = (b, sb) => sb.Append("Pulsar\nScan");
            scanAction.ValidForGroups = false;
            MyAPIGateway.TerminalControls.AddAction<IMyOreDetector>(scanAction);

            // Scan All Planets
            var planetAction = MyAPIGateway.TerminalControls.CreateAction<IMyOreDetector>("PulsarScanPlanetsAction");
            planetAction.Name           = new StringBuilder("Pulsar: Scan All Planets");
            planetAction.Icon           = @"Textures\GUI\Icons\Actions\Start.dds";
            planetAction.Action         = (b) => _gpsManager.ScanAllPlanets();
            planetAction.Writer         = (b, sb) => sb.Append("Pulsar\nPlanets");
            planetAction.ValidForGroups = false;
            MyAPIGateway.TerminalControls.AddAction<IMyOreDetector>(planetAction);

            // Clear Markers
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
        /// Controls added:
        ///   0. Version label   — confirms plugin is running and which version
        ///   1. Scan Sector     — entity-based sphere scan for ore
        ///   2. Scan All Planets — iterates all game entities for MyPlanet
        ///   3. Pulsar Scan Range — independent range slider (bypasses ~150m block cap)
        ///   4. Sector Name     — GPS label prefix (e.g. "S01")
        ///   5. Clear Markers   — resets survey session (same as Shift+T)
        ///
        /// Why a separate range slider:
        ///   The vanilla Ore Detector block definition hardcaps Range at ~150m.
        ///   SetValueFloat("Range", 2500) would be silently clamped to 150.
        ///   Pulsar reads GpsManagerService.PulsarScanRange directly, bypassing the block.
        /// </summary>
        private void AddOreDetectorControls(IMyTerminalBlock block, List<IMyTerminalControl> controls)
        {
            if (!(block is IMyOreDetector)) return;
            if (controls.Exists(x => x.Id == "PulsarVersionLabel")) return; // duplicate guard

            // --- 0: Version label ---
            var versionLabel = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlLabel, IMyOreDetector>("PulsarVersionLabel");
            versionLabel.Label              = MyStringId.GetOrCompute($"─── Pulsar Surveyor Compute v{PLUGIN_VERSION} ───");
            versionLabel.SupportsMultipleBlocks = false;
            controls.Add(versionLabel);

            // --- 1: Scan Sector button ---
            var scanBtn = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyOreDetector>("PulsarScanSector");
            scanBtn.Title  = MyStringId.GetOrCompute("Pulsar: Scan Sector for Ore");
            scanBtn.Action = (b) => _gpsManager.ForceSectorScan(b);
            controls.Add(scanBtn);

            // --- 2: Scan All Planets button ---
            var planetBtn = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyOreDetector>("PulsarScanPlanets");
            planetBtn.Title  = MyStringId.GetOrCompute("Pulsar: Scan All Planets");
            planetBtn.Action = (b) => _gpsManager.ScanAllPlanets();
            controls.Add(planetBtn);

            // --- 3: Pulsar Scan Range slider ---
            var rangeSlider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyOreDetector>("PulsarScanRange");
            rangeSlider.Title  = MyStringId.GetOrCompute("Pulsar: Scan Range");
            rangeSlider.SetLimits(50f, _configService.Data.MaxScanRange);
            rangeSlider.Getter = (b) => _gpsManager.PulsarScanRange;
            rangeSlider.Setter = (b, v) =>
            {
                _gpsManager.PulsarScanRange          = v;
                _configService.Data.PulsarScanRange  = v;
            };
            rangeSlider.Writer = (b, sb) => sb.AppendFormat("{0:N0} m", _gpsManager.PulsarScanRange);
            controls.Add(rangeSlider);

            // --- 4: Sector Name textbox ---
            var sectorBox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlTextbox, IMyOreDetector>("PulsarSectorName");
            sectorBox.Title  = MyStringId.GetOrCompute("Pulsar: Sector Name");
            sectorBox.Getter = (b) => new StringBuilder(_gpsManager.CurrentSectorName);
            sectorBox.Setter = (b, v) => _gpsManager.CurrentSectorName = v.ToString();
            controls.Add(sectorBox);

            // --- 5: Clear Markers button ---
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
            // Note: _actionsAddedToEngine stays true — AddAction is permanent,
            // cannot and should not be undone mid-session.
        }
    }
}
