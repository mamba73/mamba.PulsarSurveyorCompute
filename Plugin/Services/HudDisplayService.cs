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

        /// <summary>
        /// Draws the HUD with the provided data. If isWarning is true, it will display a prominent warning message at the top.
        /// </summary>
        /// <param name="mass"></param>
        /// <param name="maxDecel"></param>
        /// <param name="altitude"></param>
        /// <param name="range"></param>
        /// <param name="isWarning"></param> <summary>
        /// </summary>
        public void Draw(float mass, float maxDecel, double altitude, double range, bool isWarning)
        {
            _displayBuilder.Clear();

            if (isWarning)
            {
                _displayBuilder.AppendLine(">>> IMPACT IMMINENT <<<");
                _displayBuilder.AppendLine(">>>   BRAKE NOW    <<<");
                _displayBuilder.AppendLine("-----------------------");
            }

            _displayBuilder.AppendLine("=== PULSAR FLIGHT COMPUTER ===");
            _displayBuilder.AppendLine($"Mass: {mass:N0} kg");
            _displayBuilder.AppendLine($"Decel: {maxDecel:F2} m/s²");

            if (altitude >= 0) _displayBuilder.AppendLine($"Altitude: {altitude:N0} m");
            if (range > 0) _displayBuilder.AppendLine($"Laser: {range:N0} m");

            MyFontEnum font = isWarning ? MyFontEnum.Red : MyFontEnum.White;
            MyAPIGateway.Utilities.ShowNotification(_displayBuilder.ToString(), 16, font);
        }
    }
}