// Plugin/Services/HudDisplayService.cs
using System.Text;
using Plugin.Models;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;  // IMyHudNotification
using VRageMath;

namespace Plugin.Services
{
    public class HudDisplayService
    {
        private readonly ConfigService _configService;
        private readonly StringBuilder _sb = new StringBuilder();

        // Persistent notification — created once, updated every frame (no flicker/stacking)
        private IMyHudNotification _hudNote;

        public HudDisplayService(ConfigService configService)
        {
            _configService = configService;
        }

        /// <summary>
        /// Draws the Pulsar HUD. Called every tick.
        ///
        /// Sections shown:
        ///   [Always]   IMPACT IMMINENT banner (when isWarning = true)
        ///   [Always]   Flight Computer: mass, decel, altitude, gravity, laser range
        ///   [In well]  Planet approach block: planet name, altitude, gravity well distance,
        ///              gravity sustainability, escape warning
        ///   [Stationary + Full HUD] Pilot controls cheat-sheet
        /// </summary>
        public void Draw(
            float mass, float maxDecel, double altitude, double range,
            float gravityG, bool isWarning, PlanetApproachInfo approach)
        {
            int hudState = MyAPIGateway.Session.Config.HudState;
            if (hudState == 0)
            {
                _hudNote?.Hide();
                return;
            }

            _sb.Clear();

            // --- IMPACT WARNING ---
            if (isWarning)
            {
                _sb.AppendLine(">>> IMPACT IMMINENT <<<");
                _sb.AppendLine(">>>   BRAKE NOW      <<<");
                _sb.AppendLine("-----------------------");
            }

            // --- FLIGHT COMPUTER ---
            _sb.AppendLine("=== PULSAR FLIGHT COMPUTER ===");
            _sb.AppendLine($"Mass:  {mass:N0} kg");
            _sb.AppendLine($"Decel: {maxDecel:F2} m/s\u00B2");

            if (altitude >= 0)
                _sb.AppendLine($"Alt:   {altitude:N0} m");

            if (gravityG > 0.01f)
                _sb.AppendLine($"Grav:  {gravityG:F2} G");

            if (range > 0)
                _sb.AppendLine($"Lsr:   {range:N0} m");

            // --- PLANET APPROACH BLOCK ---
            // Shown whenever a planet's telemetry data is available (inside detection zone).
            if (approach != null)
            {
                _sb.AppendLine("-----------------------");
                _sb.AppendLine($"PLANET: {approach.PlanetName}");

                if (approach.InsideGravityWell)
                {
                    // Already inside — show altitude and how deep inside the well
                    double depthKm = -approach.DistToWellEdgeM / 1000.0;
                    _sb.AppendLine($"GW:    INSIDE ({depthKm:F1}km deep)");

                    // Gravity sustainability: can the ship counter gravity with its thrust?
                    if (!approach.CanEscapeGravity)
                    {
                        // CRITICAL: ship cannot fight gravity — will fall
                        float deficit = approach.GravityAccel - approach.LiveMaxDecel;
                        _sb.AppendLine($"!!! CANNOT SUSTAIN — deficit {deficit:F1} m/s\u00B2 !!!");
                        _sb.AppendLine("!!! EXIT GRAVITY WELL NOW !!!");
                    }
                    else
                    {
                        float margin = approach.LiveMaxDecel - approach.GravityAccel;
                        _sb.AppendLine($"Thrust OK — margin +{margin:F1} m/s\u00B2");
                    }
                }
                else
                {
                    // Approaching — show distance to well boundary
                    double distKm = approach.DistToWellEdgeM / 1000.0;
                    _sb.AppendLine($"GW:    {distKm:F1} km to entry");

                    // Warn early if approaching fast and the ship might struggle inside
                    if (!approach.CanEscapeGravity && approach.DistToWellEdgeM < _configService.Data.GravityWellWarnDistance)
                    {
                        _sb.AppendLine($"WARN: Insufficient thrust for {approach.PlanetName}!");
                        _sb.AppendLine($"Need {approach.GravityAccel:F1} m/s\u00B2, have {approach.LiveMaxDecel:F1}");
                    }
                }
            }

            // --- PILOT HELP (full HUD + stationary or warning) ---
            if (hudState > 1)
            {
                double speed = MyAPIGateway.Session.Player?.Controller?.ControlledEntity
                    ?.Entity?.Physics?.LinearVelocity.Length() ?? 0;

                if (speed < _configService.Data.MinSpeedForTunnel || isWarning)
                {
                    _sb.AppendLine("-----------------------");
                    _sb.AppendLine("[T]         Laser ping");
                    _sb.AppendLine("[Shift+T]   Clear GPS");
                    _sb.AppendLine("[Terminal]  Scan / Planets");
                    _sb.AppendLine("=======================");
                }
            }

            string font = isWarning || (approach != null && !approach.CanEscapeGravity && approach.InsideGravityWell)
                ? MyFontEnum.Red.ToString()
                : MyFontEnum.White.ToString();

            if (_hudNote == null)
                _hudNote = MyAPIGateway.Utilities.CreateNotification("", 0, font);

            _hudNote.Text = _sb.ToString();
            _hudNote.Font = font;
            _hudNote.ResetAliveTime();
            _hudNote.Show();
        }

        public void Hide()
        {
            _hudNote?.Hide();
        }
    }
}
