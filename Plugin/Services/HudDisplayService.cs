// Plugin/Services/HudDisplayService.cs
using System;
using System.Text;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace Plugin.Services
{
    public class HudDisplayService
    {
        private readonly ConfigService _config;
        private readonly StringBuilder _displayBuilder = new StringBuilder();

        public HudDisplayService(ConfigService config)
        {
            _config = config;
        }

        public void Draw(float mass, float maxDecel, double altitude, double range)
        {
            _displayBuilder.Clear();
            _displayBuilder.AppendLine("=== PULSAR FLIGHT COMPUTER ===");
            _displayBuilder.AppendLine($"Mass: {mass:N0} kg");
            _displayBuilder.AppendLine($"Decel: {maxDecel:F2} m/s²");

            if (altitude >= 0)
                _displayBuilder.AppendLine($"Altitude: {altitude:N0} m");

            if (range > 0)
                _displayBuilder.AppendLine($"Laser: {range:N0} m");

            // Displaying as a notification is the most stable ModAPI HUD method
            MyAPIGateway.Utilities.ShowNotification(_displayBuilder.ToString(), 16, MyFontEnum.White);
        }
    }
}