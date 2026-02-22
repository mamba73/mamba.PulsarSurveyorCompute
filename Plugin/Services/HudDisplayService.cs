// Plugin/Services/HudDisplayService.cs
using System.Text;
using Plugin.Models;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;  // IMyHudNotification lives here
using VRageMath;

namespace Plugin.Services
{
    public class HudDisplayService
    {
        private readonly ConfigService _configService;
        private readonly StringBuilder _sb = new StringBuilder();

        // FIX (flicker): Persistent notification object — created once, updated every frame.
        // Using IMyHudNotification.Show() + ResetAliveTime() keeps it alive indefinitely.
        // This avoids ShowNotification() stacking/ghosting that caused HUD flicker.
        // Namespace: VRage.Game.ModAPI (confirmed in VRage.Game.dll DLL report).
        private IMyHudNotification _hudNote;

        public HudDisplayService(ConfigService configService)
        {
            _configService = configService;
        }

        /// <summary>
        /// Draws the Pulsar HUD overlay. Called every game tick from MainPlugin.Update().
        ///
        /// FLICKER FIX: Uses a single persistent IMyHudNotification.
        ///   Created once via MyAPIGateway.Utilities.CreateNotification().
        ///   Text updated in-place each frame via .Text property.
        ///   ResetAliveTime() called each frame to keep it visible indefinitely.
        ///
        /// HUD SYNC: Respects vanilla HUD toggle (H key):
        ///   State 0 = off     → Pulsar hides too
        ///   State 1 = minimal → telemetry only
        ///   State 2 = full    → telemetry + contextual help when stationary
        /// </summary>
        public void Draw(float mass, float maxDecel, double altitude, double range, float gravityG, bool isWarning)
        {
            int hudState = MyAPIGateway.Session.Config.HudState;

            // Player turned HUD off (H key) — hide Pulsar overlay and stop updating
            if (hudState == 0)
            {
                _hudNote?.Hide();
                return;
            }

            _sb.Clear();

            // --- IMPACT WARNING BANNER ---
            if (isWarning)
            {
                _sb.AppendLine(">>> IMPACT IMMINENT <<<");
                _sb.AppendLine(">>>   BRAKE NOW      <<<");
                _sb.AppendLine("-----------------------");
            }

            _sb.AppendLine("=== PULSAR FLIGHT COMPUTER ===");

            // --- PRIMARY TELEMETRY ---
            // Shown in both minimal (1) and full (2) HUD states.
            _sb.AppendLine($"Mass:  {mass:N0} kg");
            _sb.AppendLine($"Decel: {maxDecel:F2} m/s\u00B2");   // ² via unicode — avoids encoding issues

            if (altitude >= 0)
                _sb.AppendLine($"Alt:   {altitude:N0} m");

            if (gravityG > 0.01f)
                _sb.AppendLine($"Grav:  {gravityG:F2} G");

            if (range > 0)
                _sb.AppendLine($"Lsr:   {range:N0} m");

            // --- CONTEXTUAL PILOT HELP ---
            // Only in full HUD mode when ship is nearly stationary or in danger.
            if (hudState > 1)
            {
                double speed = MyAPIGateway.Session.Player?.Controller?.ControlledEntity
                    ?.Entity?.Physics?.LinearVelocity.Length() ?? 0;

                if (speed < _configService.Data.MinSpeedForTunnel || isWarning)
                {
                    _sb.AppendLine("-----------------------");
                    _sb.AppendLine("CONTROLS:");
                    _sb.AppendLine("[T]        Laser ping");
                    _sb.AppendLine("[Shift+T]  Clear all GPS");
                    _sb.AppendLine("[Terminal] Scan Sector");
                    _sb.AppendLine("=======================");
                }
            }

            // --- FONT SELECTION ---
            string font = isWarning ? MyFontEnum.Red.ToString() : MyFontEnum.White.ToString();

            // --- PERSISTENT NOTIFICATION (flicker fix) ---
            // CreateNotification(text, aliveTime, font):
            //   aliveTime = 0 means "use default" — but we keep it alive via ResetAliveTime() each tick.
            //   This is safer than int.MaxValue which can overflow internal timers on some SE versions.
            if (_hudNote == null)
                _hudNote = MyAPIGateway.Utilities.CreateNotification("", 0, font);

            _hudNote.Text = _sb.ToString();
            _hudNote.Font = font;
            _hudNote.ResetAliveTime(); // Restart the timer so it stays visible this frame
            _hudNote.Show();
        }

        /// <summary>
        /// Hides the overlay. Called from MainPlugin.Dispose() to clean up on session end.
        /// </summary>
        public void Hide()
        {
            _hudNote?.Hide();
        }
    }
}
