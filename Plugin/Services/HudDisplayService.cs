// Plugin/Services/HudDisplayService.cs
using System;
using System.Text;
using Plugin.Models;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;  // IMyHudNotification
using VRageMath;

namespace Plugin.Services
{
    /// <summary>
    /// Manages two HUD outputs:
    ///
    ///   1. COCKPIT LCD (IMyTextSurface, Surface 0) — full avionics display.
    ///      Written every frame. ContentType set every frame (SE resets on cockpit entry).
    ///      Re-bound on cockpit change or when surface is null (fixes black screen bug).
    ///
    ///   2. SCREEN NOTIFICATION (IMyHudNotification) — compact one-liner.
    ///      SE API does not allow repositioning notifications — they always appear
    ///      at screen center-bottom. HudPosition config is not functional via this API.
    ///      The line is kept short to not obscure scan result notifications.
    ///
    /// CONFIG FILTERING:
    ///   HudShowMass, HudShowDecel, HudShowAlt, HudShowGravity, HudShowLaser, HudShowPlanet
    ///   control which fields appear. Toggle in config.xml without recompiling.
    /// </summary>
    public class HudDisplayService
    {
        private readonly ConfigService _configService;
        private readonly StringBuilder _sb = new StringBuilder();

        private Sandbox.ModAPI.Ingame.IMyTextSurface _cockpitSurface;
        private IMyShipController _lastCockpit;
        private IMyHudNotification _hudNote;

        public HudDisplayService(ConfigService configService)
        {
            _configService = configService;
        }

        public void Draw(
            IMyShipController ship,
            float mass, float maxDecel, double altitude,
            double range, float gravityG, bool isWarning,
            PlanetApproachInfo approach)
        {
            // Ship null = not in cockpit → hide everything
            if (ship == null)
            {
                _hudNote?.Hide();
                ClearSurface();
                _lastCockpit = null;
                return;
            }

            int hudState = MyAPIGateway.Session.Config.HudState;
            if (hudState == 0)
            {
                _hudNote?.Hide();
                ClearSurface();
                return;
            }

            // REBIND: trigger on cockpit change OR when surface is null (re-entry fix)
            if (ship != _lastCockpit || _cockpitSurface == null)
            {
                ClearSurface();
                _lastCockpit = ship;
                var provider = ship as Sandbox.ModAPI.Ingame.IMyTextSurfaceProvider;
                if (provider != null && provider.SurfaceCount > 0)
                    _cockpitSurface = provider.GetSurface(0);
            }

            var cfg = _configService.Data;

            // --- COCKPIT LCD: full avionics panel ---
            if (_cockpitSurface != null)
            {
                _sb.Clear();
                BuildLcdText(mass, maxDecel, altitude, range, gravityG, isWarning, approach, cfg, hudState);

                // ContentType must be set every frame — SE resets it on cockpit entry
                _cockpitSurface.ContentType     = ContentType.TEXT_AND_IMAGE;
                _cockpitSurface.FontColor       = isWarning ? Color.OrangeRed : new Color(0, 230, 180);
                _cockpitSurface.BackgroundColor  = new Color(0, 8, 18);
                _cockpitSurface.FontSize        = 1.0f;
                _cockpitSurface.Font            = "Monospace";
                _cockpitSurface.Alignment       = TextAlignment.LEFT;
                _cockpitSurface.WriteText(_sb.ToString());
            }

            // --- SCREEN NOTIFICATION: compact one-liner ---
            // Kept short so scan result popups at the bottom remain visible
            string status = BuildStatusLine(maxDecel, altitude, gravityG, isWarning, approach, cfg);
            string font   = isWarning ? MyFontEnum.Red.ToString() : MyFontEnum.White.ToString();

            if (_hudNote == null)
                _hudNote = MyAPIGateway.Utilities.CreateNotification("", 0, font);

            _hudNote.Font = font;
            _hudNote.Text = status;
            _hudNote.ResetAliveTime();
            _hudNote.Show();
        }

        // -----------------------------------------------------------------------
        // LCD TEXT — full avionics layout
        // -----------------------------------------------------------------------

        private void BuildLcdText(
            float mass, float maxDecel, double altitude, double range,
            float gravityG, bool isWarning, PlanetApproachInfo ap, Config cfg, int hudState)
        {
            if (isWarning)
            {
                _sb.AppendLine("!!! IMPACT IMMINENT !!!");
                _sb.AppendLine("!!!   BRAKE NOW      !!!");
                _sb.AppendLine("------------------------");
            }

            _sb.AppendLine("== PULSAR FLIGHT COMPUTER ==");

            if (cfg.HudShowMass)
                _sb.AppendLine($"Mass:  {mass:N0} kg");

            if (cfg.HudShowDecel)
                _sb.AppendLine($"Decel: {maxDecel:F2} m/s\u00B2");

            if (cfg.HudShowAlt && altitude >= 0)
                _sb.AppendLine($"Alt:   {altitude:N0} m");

            if (cfg.HudShowGravity && gravityG > 0.01f)
                _sb.AppendLine($"Grav:  {gravityG:F2} G");

            if (cfg.HudShowLaser && range > 0)
                _sb.AppendLine($"Lsr:   {range:N0} m");

            if (cfg.HudShowPlanet && ap != null)
            {
                _sb.AppendLine("------------------------");
                _sb.AppendLine($"PLANET: {ap.PlanetName}");

                if (ap.InsideGravityWell)
                {
                    double depthKm = -ap.DistToWellEdgeM / 1000.0;
                    _sb.AppendLine($"GW:  INSIDE ({depthKm:F1} km deep)");

                    if (!ap.CanEscapeGravity)
                    {
                        float deficit = ap.GravityAccel - ap.LiveMaxDecel;
                        _sb.AppendLine($"!!! NO ESCAPE: -{deficit:F1} m/s\u00B2 !!!");
                        _sb.AppendLine("!!! EXIT GRAVITY WELL NOW !!!");
                    }
                    else
                    {
                        float margin = ap.LiveMaxDecel - ap.GravityAccel;
                        _sb.AppendLine($"Thrust margin: +{margin:F1} m/s\u00B2 OK");
                    }
                }
                else
                {
                    double distKm = ap.DistToWellEdgeM / 1000.0;
                    _sb.AppendLine($"GW:  {distKm:F1} km to entry");

                    if (!ap.CanEscapeGravity && ap.DistToWellEdgeM < _configService.Data.GravityWellWarnDistance)
                        _sb.AppendLine($"WARN: Insufficient thrust!");
                }
            }

            if (hudState > 1)
            {
                double speed = MyAPIGateway.Session.Player?.Controller?.ControlledEntity
                    ?.Entity?.Physics?.LinearVelocity.Length() ?? 0;

                if (speed < _configService.Data.MinSpeedForTunnel || isWarning)
                {
                    _sb.AppendLine("------------------------");
                    _sb.AppendLine("[T]        Laser ping");
                    _sb.AppendLine("[Shift+T]  Clear GPS");
                    _sb.AppendLine("[Terminal] Scan/Planets");
                }
            }
        }

        // -----------------------------------------------------------------------
        // SCREEN NOTIFICATION — compact one-liner, minimal width
        // -----------------------------------------------------------------------

        private static string BuildStatusLine(
            float maxDecel, double altitude, float gravityG,
            bool isWarning, PlanetApproachInfo ap, Config cfg)
        {
            var sb = new StringBuilder();

            if (isWarning) sb.Append("!! BRAKE !! ");

            if (cfg.HudShowDecel)   sb.Append($"{maxDecel:F1}m/s\u00B2 ");
            if (cfg.HudShowAlt && altitude >= 0) sb.Append($"Alt:{altitude:N0}m ");
            if (cfg.HudShowGravity && gravityG > 0.01f) sb.Append($"G:{gravityG:F2} ");

            if (cfg.HudShowPlanet && ap != null)
            {
                if (ap.InsideGravityWell)
                {
                    string ok = ap.CanEscapeGravity ? "\u2713" : "!!";
                    sb.Append($"| {ap.PlanetName}[{ok}]");
                }
                else
                {
                    sb.Append($"| {ap.PlanetName} {ap.DistToWellEdgeM / 1000.0:F0}km");
                }
            }

            return sb.Length > 0 ? $"[PSC] {sb}" : "[PSC]";
        }

        private void ClearSurface()
        {
            try { _cockpitSurface?.WriteText(""); } catch { }
            _cockpitSurface = null;
        }

        public void Hide()
        {
            _hudNote?.Hide();
            ClearSurface();
            _lastCockpit = null;
        }
    }
}
