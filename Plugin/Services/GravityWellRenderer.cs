// Plugin/Services/GravityWellRenderer.cs
using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace Plugin.Services
{
    /// <summary>
    /// Renders a virtual gravity well sphere around nearby planets.
    ///
    /// APPEARANCE:
    ///   Three orthogonal great circles (XY, YZ, XZ planes) drawn at the planet's
    ///   gravity well outer radius (AverageRadius × 2). Shown only in configured views.
    ///   If two planets are nearby (e.g. between Moon and Earth), both spheres render.
    ///
    /// VISIBILITY CONDITIONS (all must be true):
    ///   - Config.GravityWellShowCockpit = true (for first-person / cockpit view)
    ///   - Config.GravityWellShowExternal = true (for external / third-person view)
    ///   - Ship is within Config.GravityWellShowRadius of the planet center
    ///     (0 = show whenever inside 2× well radius)
    ///
    /// PERFORMANCE:
    ///   Each circle = Config.GravityWellSegments line segments (default 64).
    ///   3 circles × 64 segments = 192 AddLineBillboard calls per planet.
    ///   Disable GravityWellShowExternal if you see lag in external view.
    /// </summary>
    public class GravityWellRenderer
    {
        private readonly ConfigService _configService;
        private readonly MyStringId    _material = MyStringId.GetOrCompute("Square");

        public GravityWellRenderer(ConfigService configService)
        {
            _configService = configService;
        }

        /// <summary>
        /// Called every tick from MainPlugin. Draws gravity well spheres for nearby planets.
        /// 'ship' may be null (not in cockpit) — still renders in external view if configured.
        /// </summary>
        public void Draw(IMyShipController ship, List<MyPlanet> nearbyPlanets)
        {
            if (nearbyPlanets == null || nearbyPlanets.Count == 0) return;

            var cfg = _configService.Data;

            // Determine current camera mode
            bool isCockpit = MyAPIGateway.Session.CameraController == ship;

            bool shouldDraw = (isCockpit  && cfg.GravityWellShowCockpit)
                           || (!isCockpit && cfg.GravityWellShowExternal);
            if (!shouldDraw) return;

            Vector3D cameraPos = MyAPIGateway.Session.Camera.Position;
            Vector4  color     = cfg.GravityWellColor.ToVector4();

            foreach (var planet in nearbyPlanets)
            {
                if (planet == null || planet.Closed) continue;

                Vector3D center    = planet.PositionComp.GetPosition();
                float    wellR     = planet.AverageRadius * 2f;
                double   showRadius = cfg.GravityWellShowRadius > 0
                    ? cfg.GravityWellShowRadius
                    : wellR * 1.05; // default: show when inside/near the well

                double distToCenter = Vector3D.Distance(cameraPos, center);
                if (distToCenter > showRadius) continue;

                // Draw 3 orthogonal great circles to approximate a sphere
                DrawCircle(center, wellR, Vector3D.UnitX, Vector3D.UnitY, color, cfg.GravityWellSegments);
                DrawCircle(center, wellR, Vector3D.UnitY, Vector3D.UnitZ, color, cfg.GravityWellSegments);
                DrawCircle(center, wellR, Vector3D.UnitX, Vector3D.UnitZ, color, cfg.GravityWellSegments);
            }
        }

        /// <summary>
        /// Draws a single great circle (large circle on sphere surface).
        ///
        /// The circle lies in the plane defined by (axis1, axis2).
        /// Each segment is drawn as a short AddLineBillboard call.
        ///
        /// Alpha is distance-faded: when camera is far from the circle, it fades out.
        /// This prevents the well from being distractingly bright when approaching from far.
        /// </summary>
        private void DrawCircle(
            Vector3D center, float radius,
            Vector3D axis1, Vector3D axis2,
            Vector4 baseColor, int segments)
        {
            if (segments < 6) segments = 6;

            double cameraDistToCenter = Vector3D.Distance(
                MyAPIGateway.Session.Camera.Position, center);

            // Fade alpha based on how close we are relative to the well radius.
            // At center (dist=0): full alpha. At well edge (dist=radius): very faint.
            float distRatio = (float)Math.Min(1.0, cameraDistToCenter / radius);
            float alpha     = baseColor.W * (1f - distRatio * 0.7f);
            if (alpha < 0.005f) return;

            Vector4 color = new Vector4(baseColor.X, baseColor.Y, baseColor.Z, alpha);
            double  step  = Math.PI * 2.0 / segments;

            Vector3D prev = center + axis1 * radius;

            for (int i = 1; i <= segments; i++)
            {
                double   angle = i * step;
                Vector3D cur   = center + axis1 * (Math.Cos(angle) * radius)
                                        + axis2 * (Math.Sin(angle) * radius);

                Vector3D dir = cur - prev;
                float    len = (float)dir.Length();
                if (len < 0.001f) { prev = cur; continue; }

                MyTransparentGeometry.AddLineBillboard(
                    _material, color,
                    prev, (Vector3)(dir / len), len, 15f);  // 15m thick line visible from far

                prev = cur;
            }
        }
    }
}
