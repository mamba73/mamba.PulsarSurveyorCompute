// Plugin/Services/InputHandlerService.cs
using System;
using Sandbox.ModAPI;
using VRage.Input;
using VRage.Game;

namespace Plugin.Services
{
    public class InputHandlerService
    {
        private readonly ConfigService _config;
        private readonly PhysicsService _physics;
        private readonly GpsManagerService _gpsManager; // Added this

        public InputHandlerService(ConfigService config, PhysicsService physics, GpsManagerService gpsManager)
        {
            _config = config;
            _physics = physics;
            _gpsManager = gpsManager; // Injected
        }

        public void Update(IMyShipController ship, ref double lastRange)
        {
            if (ship == null) return;

            MyKeys hotkey;
            if (Enum.TryParse(_config.Data.RangefinderHotkey, true, out hotkey))
            {
                // Shift + Hotkey to Clear
                if (MyAPIGateway.Input.IsAnyShiftKeyPressed() && MyAPIGateway.Input.IsNewKeyPressed(hotkey))
                {
                    _gpsManager.ClearScanData();
                    return;
                }

                // Normal Hotkey for Rangefinder
                if (MyAPIGateway.Input.IsNewKeyPressed(hotkey))
                {
                    lastRange = _physics.RaycastDistance(ship);
                    if (lastRange <= 0)
                        MyAPIGateway.Utilities.ShowNotification("No target in range", 2000, MyFontEnum.Red);
                }
            }
        }
    }
}