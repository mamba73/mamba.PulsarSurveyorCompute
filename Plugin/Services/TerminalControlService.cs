// Plugin/Services/TerminalControlService.cs
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
        private readonly ConfigService _configService;
        private bool _controlsRegistered = false;

        public TerminalControlService(GpsManagerService gpsManager, ConfigService configService)
        {
            _gpsManager    = gpsManager;
            _configService = configService;
        }

        /// <summary>Called every tick until registration succeeds.</summary>
        public void Initialize()
        {
            if (_controlsRegistered || MyAPIGateway.TerminalControls == null) return;
            MyAPIGateway.TerminalControls.CustomControlGetter += AddOreDetectorControls;
            RegisterToolbarActions();
            _controlsRegistered = true;
        }

        /// <summary>
        /// Registers G-menu toolbar actions for the Ore Detector.
        /// These can be dragged into the ship's hotbar.
        /// </summary>
        private void RegisterToolbarActions()
        {
            // Action 1: Scan Sector for Ore
            var scanAction = MyAPIGateway.TerminalControls.CreateAction<IMyOreDetector>("PulsarScanSectorAction");
            scanAction.Name            = new StringBuilder("Pulsar: Scan Sector");
            scanAction.Icon            = @"Textures\GUI\Icons\Actions\Start.dds";
            scanAction.Action          = (b) => _gpsManager.ForceSectorScan(b);
            scanAction.Writer          = (b, sb) => sb.Append("Scan");
            scanAction.ValidForGroups  = false;
            MyAPIGateway.TerminalControls.AddAction<IMyOreDetector>(scanAction);

            // Action 2: Scan All Planets (global entity search)
            var planetAction = MyAPIGateway.TerminalControls.CreateAction<IMyOreDetector>("PulsarScanPlanetsAction");
            planetAction.Name           = new StringBuilder("Pulsar: Scan All Planets");
            planetAction.Icon           = @"Textures\GUI\Icons\Actions\Start.dds";
            planetAction.Action         = (b) => _gpsManager.ScanAllPlanets();
            planetAction.Writer         = (b, sb) => sb.Append("Planets");
            planetAction.ValidForGroups = false;
            MyAPIGateway.TerminalControls.AddAction<IMyOreDetector>(planetAction);

            // Action 3: Clear All Markers
            var clearAction = MyAPIGateway.TerminalControls.CreateAction<IMyOreDetector>("PulsarClearMarkersAction");
            clearAction.Name           = new StringBuilder("Pulsar: Clear Markers");
            clearAction.Icon           = @"Textures\GUI\Icons\Actions\Reset.dds";
            clearAction.Action         = (b) => _gpsManager.ClearAllMarkers();
            clearAction.Writer         = (b, sb) => sb.Append("Clear");
            clearAction.ValidForGroups = false;
            MyAPIGateway.TerminalControls.AddAction<IMyOreDetector>(clearAction);
        }

        /// <summary>
        /// Injects Pulsar controls into the Ore Detector terminal panel.
        ///
        /// Control list:
        ///   1. Scan Sector for Ore    — entity-based full sphere scan
        ///   2. Scan All Planets       — global entity search for MyPlanet
        ///   3. Pulsar Scan Range      — Pulsar's OWN range (independent of block cap)
        ///   4. Sector Name            — GPS label prefix
        ///   5. Clear All Markers      — reset session
        ///
        /// WHY a separate Pulsar range slider (not using block "Range"):
        ///   The vanilla Ore Detector block has a hardcoded max range of ~150m in its
        ///   block definition. SetValueFloat("Range", 2500) would be silently clamped.
        ///   Pulsar's entity-based scan uses GpsManagerService.PulsarScanRange directly,
        ///   bypassing the block definition entirely.
        /// </summary>
        private void AddOreDetectorControls(IMyTerminalBlock block, List<IMyTerminalControl> controls)
        {
            if (!(block is IMyOreDetector)) return;
            if (controls.Exists(x => x.Id == "PulsarScanSector")) return; // already added

            // --- 1: Scan Sector Button ---
            var scanBtn = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyOreDetector>("PulsarScanSector");
            scanBtn.Title  = MyStringId.GetOrCompute("Pulsar: Scan Sector for Ore");
            scanBtn.Action = (b) => _gpsManager.ForceSectorScan(b);
            controls.Add(scanBtn);

            // --- 2: Scan All Planets Button ---
            var planetBtn = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyOreDetector>("PulsarScanPlanets");
            planetBtn.Title  = MyStringId.GetOrCompute("Pulsar: Scan All Planets");
            planetBtn.Action = (b) => _gpsManager.ScanAllPlanets();
            controls.Add(planetBtn);

            // --- 3: Pulsar Scan Range Slider ---
            // Reads/writes GpsManagerService.PulsarScanRange (NOT the block's vanilla Range).
            // Upper limit = Config.MaxScanRange (default 2500m, configurable in config.xml).
            var rangeSlider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyOreDetector>("PulsarScanRange");
            rangeSlider.Title  = MyStringId.GetOrCompute("Pulsar: Scan Range");
            rangeSlider.SetLimits(50f, _configService.Data.MaxScanRange);
            rangeSlider.Getter = (b) => _gpsManager.PulsarScanRange;
            rangeSlider.Setter = (b, v) =>
            {
                _gpsManager.PulsarScanRange = v;
                _configService.Data.PulsarScanRange = v; // persist to config
            };
            rangeSlider.Writer = (b, sb) => sb.AppendFormat("{0:N0} m  (Pulsar)", _gpsManager.PulsarScanRange);
            controls.Add(rangeSlider);

            // --- 4: Sector Name Textbox ---
            var sectorBox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlTextbox, IMyOreDetector>("PulsarSectorName");
            sectorBox.Title  = MyStringId.GetOrCompute("Pulsar: Sector Name");
            sectorBox.Getter = (b) => new StringBuilder(_gpsManager.CurrentSectorName);
            sectorBox.Setter = (b, v) => _gpsManager.CurrentSectorName = v.ToString();
            controls.Add(sectorBox);

            // --- 5: Clear Markers Button ---
            var clearBtn = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyOreDetector>("PulsarClearMarkers");
            clearBtn.Title  = MyStringId.GetOrCompute("Pulsar: Clear All Markers");
            clearBtn.Action = (b) => _gpsManager.ClearAllMarkers();
            controls.Add(clearBtn);
        }

        public void Terminate()
        {
            if (_controlsRegistered && MyAPIGateway.TerminalControls != null)
                MyAPIGateway.TerminalControls.CustomControlGetter -= AddOreDetectorControls;
        }
    }
}
