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
        // Calling it twice on world reload creates duplicates that break both entries.
        private static bool _actionsAddedToEngine = false;

        // INSTANCE: CustomControlGetter hook is session-scoped.
        private bool _hookRegistered = false;

        // Pre-created control list — created ONCE, reused in getter callback.
        //
        // WHY PRE-CREATE:
        //   SE's CreateControl<>() registers a control ID globally in its terminal system.
        //   Calling CreateControl<>() with the same ID a second time (e.g. on panel re-open)
        //   returns a broken/null object. Controls must be created once and the SAME instances
        //   reused in every CustomControlGetter invocation.
        //   This is the same pattern BlockRenamer uses (ControlsListMain).
        private List<IMyTerminalControl> _controls;

        public TerminalControlService(GpsManagerService gpsManager, ConfigService configService)
        {
            _gpsManager    = gpsManager;
            _configService = configService;
        }

        /// <summary>
        /// Called every tick from Update() until registration succeeds.
        /// Safe here — MyAPIGateway APIs require an active session.
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

                // Create all controls once and store them
                _controls = BuildControlList();

                MyAPIGateway.TerminalControls.CustomControlGetter += OnGetControls;
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
                                $"[Surveyor Compute v{ver}] Loaded.",
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

        // -----------------------------------------------------------------------
        // CONTROL GETTER — called by SE every time a terminal panel opens
        // -----------------------------------------------------------------------

        private void OnGetControls(IMyTerminalBlock block, List<IMyTerminalControl> controls)
        {
            if (!(block is IMyOreDetector)) return;
            if (_controls == null) return;

            // Duplicate guard: check if our first control is already in the list
            if (controls.Exists(c => c.Id == "PSC_Label")) return;

            foreach (var ctrl in _controls)
                controls.Add(ctrl);
        }

        // -----------------------------------------------------------------------
        // CONTROL LIST — built once on session start
        // -----------------------------------------------------------------------

        private List<IMyTerminalControl> BuildControlList()
        {
            var list = new List<IMyTerminalControl>();
            string ver = _configService?.Data?.PluginVersion ?? "?";

            // --- SEPARATOR ---
            var sep = MyAPIGateway.TerminalControls.CreateControl<
                IMyTerminalControlSeparator, IMyOreDetector>("PSC_Sep");
            sep.Enabled = b => true;
            sep.Visible = b => true;
            list.Add(sep);

            // --- VERSION LABEL ---
            var label = MyAPIGateway.TerminalControls.CreateControl<
                IMyTerminalControlLabel, IMyOreDetector>("PSC_Label");
            label.Label = MyStringId.GetOrCompute($"Surveyor Compute v{ver}");
            label.Enabled = b => true;
            label.Visible = b => true;
            list.Add(label);

            // --- SCAN SECTOR BUTTON ---
            var scanBtn = MyAPIGateway.TerminalControls.CreateControl<
                IMyTerminalControlButton, IMyOreDetector>("PSC_ScanSector");
            scanBtn.Title   = MyStringId.GetOrCompute("Scan Sector for Ore");
            scanBtn.Enabled = b => true;
            scanBtn.Visible = b => true;
            scanBtn.Action  = b => _gpsManager.ForceSectorScan(b);
            list.Add(scanBtn);

            // --- SCAN ALL PLANETS BUTTON ---
            var planetBtn = MyAPIGateway.TerminalControls.CreateControl<
                IMyTerminalControlButton, IMyOreDetector>("PSC_ScanPlanets");
            planetBtn.Title   = MyStringId.GetOrCompute("Scan All Planets");
            planetBtn.Enabled = b => true;
            planetBtn.Visible = b => true;
            planetBtn.Action  = b => _gpsManager.ScanAllPlanets();
            list.Add(planetBtn);

            // --- SCAN RANGE SLIDER ---
            var slider = MyAPIGateway.TerminalControls.CreateControl<
                IMyTerminalControlSlider, IMyOreDetector>("PSC_ScanRange");
            slider.Title   = MyStringId.GetOrCompute("Scan Range");
            slider.Enabled = b => true;
            slider.Visible = b => true;
            slider.SetLimits(100f, _configService.Data.MaxScanRange);
            slider.Getter  = b => _gpsManager.PulsarScanRange;
            slider.Setter  = (b, v) =>
            {
                _gpsManager.PulsarScanRange         = v;
                _configService.Data.PulsarScanRange = v;
            };
            slider.Writer  = (b, sb) => sb.AppendFormat("{0:N0} m", _gpsManager.PulsarScanRange);
            list.Add(slider);

            // --- SECTOR NAME TEXTBOX ---
            var sectorBox = MyAPIGateway.TerminalControls.CreateControl<
                IMyTerminalControlTextbox, IMyOreDetector>("PSC_SectorName");
            sectorBox.Title   = MyStringId.GetOrCompute("Sector Name (GPS prefix)");
            sectorBox.Enabled = b => true;
            sectorBox.Visible = b => true;
            sectorBox.Getter  = b => new StringBuilder(_gpsManager.CurrentSectorName);
            sectorBox.Setter  = (b, v) => _gpsManager.CurrentSectorName = v.ToString();
            list.Add(sectorBox);

            // --- CLEAR MARKERS BUTTON ---
            var clearBtn = MyAPIGateway.TerminalControls.CreateControl<
                IMyTerminalControlButton, IMyOreDetector>("PSC_ClearMarkers");
            clearBtn.Title   = MyStringId.GetOrCompute("Clear All GPS Markers");
            clearBtn.Enabled = b => true;
            clearBtn.Visible = b => true;
            clearBtn.Action  = b => _gpsManager.ClearAllMarkers();
            list.Add(clearBtn);

            return list;
        }

        // -----------------------------------------------------------------------
        // TOOLBAR ACTIONS (G-menu)
        // -----------------------------------------------------------------------

        private void RegisterToolbarActions()
        {
            var scanAction = MyAPIGateway.TerminalControls.CreateAction<IMyOreDetector>("PSC_ScanSectorAction");
            scanAction.Name           = new StringBuilder("Surveyor: Scan Sector");
            scanAction.Icon           = @"Textures\GUI\Icons\Actions\Start.dds";
            scanAction.Action         = b => _gpsManager.ForceSectorScan(b);
            scanAction.Writer         = (b, sb) => sb.Append("PSC\nScan");
            scanAction.ValidForGroups = false;
            MyAPIGateway.TerminalControls.AddAction<IMyOreDetector>(scanAction);

            var planetAction = MyAPIGateway.TerminalControls.CreateAction<IMyOreDetector>("PSC_ScanPlanetsAction");
            planetAction.Name           = new StringBuilder("Surveyor: Scan Planets");
            planetAction.Icon           = @"Textures\GUI\Icons\Actions\Start.dds";
            planetAction.Action         = b => _gpsManager.ScanAllPlanets();
            planetAction.Writer         = (b, sb) => sb.Append("PSC\nPlanets");
            planetAction.ValidForGroups = false;
            MyAPIGateway.TerminalControls.AddAction<IMyOreDetector>(planetAction);

            var clearAction = MyAPIGateway.TerminalControls.CreateAction<IMyOreDetector>("PSC_ClearMarkersAction");
            clearAction.Name           = new StringBuilder("Surveyor: Clear Markers");
            clearAction.Icon           = @"Textures\GUI\Icons\Actions\Reset.dds";
            clearAction.Action         = b => _gpsManager.ClearAllMarkers();
            clearAction.Writer         = (b, sb) => sb.Append("PSC\nClear");
            clearAction.ValidForGroups = false;
            MyAPIGateway.TerminalControls.AddAction<IMyOreDetector>(clearAction);
        }

        public void Terminate()
        {
            if (_hookRegistered && MyAPIGateway.TerminalControls != null)
            {
                MyAPIGateway.TerminalControls.CustomControlGetter -= OnGetControls;
                _hookRegistered = false;
            }
            _controls = null;
        }
    }
}
