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

        public InputHandlerService(ConfigService config, PhysicsService physics)
        {
            _config = config;
            _physics = physics;
        }

        /// <summary>
        /// Handles user input for tools like the Laser Rangefinder.
        /// </summary>
        public void Update(IMyShipController ship, ref double lastRange)
        {
            if (ship == null) return;

            MyKeys hotkey;
            if (Enum.TryParse(_config.Data.RangefinderHotkey, true, out hotkey))
            {
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