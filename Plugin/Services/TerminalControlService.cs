// Plugin/Services/TerminalControlService.cs
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game.ModAPI;
using VRage.Utils;
using Plugin.Models;
using Sandbox.ModAPI.Interfaces;

namespace Plugin.Services
{
    public class TerminalControlService
    {
        private readonly GpsManagerService _gpsManager;
        private readonly ConfigService _configService;

        /// <summary>
        /// Guard: ensures controls and actions are registered exactly once.
        /// MyAPIGateway.TerminalControls is not available at Init() — becomes ready a few ticks later.
        /// </summary>
        private bool _controlsRegistered = false;

        public TerminalControlService(GpsManagerService gpsManager, ConfigService configService)
        {
            _gpsManager    = gpsManager;
            _configService = configService;
        }

        /// <summary>
        /// Called every tick from MainPlugin.Update() until controls are registered.
        /// Safe to call repeatedly — exits immediately after successful first registration.
        /// </summary>
        public void Initialize()
        {
            if (_controlsRegistered || MyAPIGateway.TerminalControls == null) return;

            MyAPIGateway.TerminalControls.CustomControlGetter += AddOreDetectorControls;
            RegisterToolbarActions();
            _controlsRegistered = true;
        }

        /// <summary>
        /// Registers a toolbar-compatible IMyTerminalAction for the Ore Detector.
        /// This allows "Pulsar: Scan Sector" to be dragged into the G-Menu toolbar
        /// and bound to a hotbar slot on any ship.
        /// </summary>
        private void RegisterToolbarActions()
        {
            // "Scan Sector for Ore" toolbar action
            var scanAction = MyAPIGateway.TerminalControls.CreateAction<IMyOreDetector>("PulsarScanSectorAction");
            scanAction.Name    = new StringBuilder("Pulsar: Scan Sector");
            scanAction.Icon    = @"Textures\GUI\Icons\Actions\Start.dds";
            scanAction.Action  = (b) => _gpsManager.ForceSectorScan(b);
            scanAction.Writer  = (b, sb) => sb.Append("Scan");
            scanAction.ValidForGroups = false;
            MyAPIGateway.TerminalControls.AddAction<IMyOreDetector>(scanAction);

            // "Clear All GPS Markers" toolbar action
            var clearAction = MyAPIGateway.TerminalControls.CreateAction<IMyOreDetector>("PulsarClearMarkersAction");
            clearAction.Name   = new StringBuilder("Pulsar: Clear Markers");
            clearAction.Icon   = @"Textures\GUI\Icons\Actions\Reset.dds";
            clearAction.Action = (b) => _gpsManager.ClearAllMarkers();
            clearAction.Writer = (b, sb) => sb.Append("Clear");
            clearAction.ValidForGroups = false;
            MyAPIGateway.TerminalControls.AddAction<IMyOreDetector>(clearAction);
        }

        /// <summary>
        /// Injects Pulsar controls into the Ore Detector terminal panel.
        /// Controls are added only once per block (duplicate guard by control ID).
        ///
        /// Added controls:
        ///   1. "Scan Sector for Ore" button — triggers a full 26-direction sphere scan
        ///   2. "Surveyor Range" slider     — adjusts detector range (max from config)
        ///   3. "Current Sector" textbox    — sets the GPS label prefix (e.g. "S01")
        ///   4. "Clear All Markers" button  — same as Shift+T
        /// </summary>
        private void AddOreDetectorControls(IMyTerminalBlock block, List<IMyTerminalControl> controls)
        {
            if (!(block is IMyOreDetector)) return;
            if (controls.Exists(x => x.Id == "PulsarScanSector")) return; // already registered

            // --- CONTROL 1: Scan Sector Button ---
            // Primary survey action — fires the full sphere-of-rays scan.
            // Can also be assigned to the ship toolbar via G-Menu (see RegisterToolbarActions).
            var scanButton = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyOreDetector>("PulsarScanSector");
            scanButton.Title  = MyStringId.GetOrCompute("Pulsar: Scan Sector for Ore");
            scanButton.Action = (b) => _gpsManager.ForceSectorScan(b);
            controls.Add(scanButton);

            // --- CONTROL 2: Surveyor Range Slider ---
            // Upper limit reads from Config.MaxDetectorRange — nothing hardcoded.
            // Uses the detector's native "Range" float property for SE compatibility.
            var rangeSlider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyOreDetector>("PulsarSurveyorRange");
            rangeSlider.Title  = MyStringId.GetOrCompute("Pulsar: Surveyor Range");
            rangeSlider.SetLimits(50f, _configService.Data.MaxDetectorRange);
            rangeSlider.Getter = (b) => b.GetValueFloat("Range");
            rangeSlider.Setter = (b, v) => b.SetValueFloat("Range", v);
            rangeSlider.Writer = (b, sb) => sb.AppendFormat("{0:N0} m", b.GetValueFloat("Range"));
            controls.Add(rangeSlider);

            // --- CONTROL 3: Sector Name Textbox ---
            // Sets the prefix for GPS labels created during this session.
            // Example: "S01" → "[Pulsar] S01 A01 (Iron, Gold)"
            var sectorInput = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlTextbox, IMyOreDetector>("PulsarSectorName");
            sectorInput.Title  = MyStringId.GetOrCompute("Pulsar: Sector Name");
            sectorInput.Getter = (b) => new StringBuilder(_gpsManager.CurrentSectorName);
            sectorInput.Setter = (b, v) => _gpsManager.CurrentSectorName = v.ToString();
            controls.Add(sectorInput);

            // --- CONTROL 4: Clear Markers Button ---
            // Resets the entire survey session from the terminal panel.
            var clearButton = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyOreDetector>("PulsarClearMarkers");
            clearButton.Title  = MyStringId.GetOrCompute("Pulsar: Clear All Markers");
            clearButton.Action = (b) => _gpsManager.ClearAllMarkers();
            controls.Add(clearButton);
        }

        /// <summary>
        /// Unregisters all hooks. Called during plugin Dispose() to prevent null-refs after session ends.
        /// </summary>
        public void Terminate()
        {
            if (_controlsRegistered && MyAPIGateway.TerminalControls != null)
                MyAPIGateway.TerminalControls.CustomControlGetter -= AddOreDetectorControls;
        }
    }
}
