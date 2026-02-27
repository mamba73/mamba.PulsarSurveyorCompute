// Plugin/Utils/RenderUtils.cs
using System;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace Plugin.Utils
{
    public static class RenderUtils
    {
        /// <summary>
        /// Renders an animated braking tunnel along the VELOCITY vector (not ship forward).
        ///
        /// RING ORIENTATION FIX:
        ///   Rings must lie in a plane PERPENDICULAR to the velocity vector.
        ///   Previously used ship.WorldMatrix.Up/Left which caused "flat glass" appearance
        ///   when drifting sideways or vertically — the ring plane was not facing the travel direction.
        ///   Fix: compute two basis vectors orthogonal to velocity using cross products.
        ///     ringRight = normalize(velocity × worldUp)   [handle parallel case]
        ///     ringUp    = normalize(ringRight × velocity)
        ///   These always define a plane perpendicular to velocity regardless of ship heading.
        ///
        /// BACKWARD MOVEMENT:
        ///   Rings draw behind the ship when velocity is backward — correct behavior.
        ///   Red color = collision within stopping distance, NOT direction judgment.
        ///   If moving backward near terrain, red is correct and expected.
        ///
        /// ANIMATION:
        ///   Position-based scroll offset (worldPos projected onto velocity, mod spacing).
        ///   Rings appear to approach the ship continuously without timer state.
        ///
        /// TRANSPARENCY:
        ///   Quadratic fade: alpha = baseAlpha × (1 − t²) where t = d/length.
        ///   Near rings most visible, far rings ghost out — "virtual" appearance.
        /// </summary>
        public static void DrawTunnel(
            IMyShipController ship,
            double length,
            Color color,
            float baseAlpha,
            double halfWidth,
            double halfHeight,
            string materialName,
            float thickness,
            float ringSpacing)
        {
            if (length <= 0 || ringSpacing <= 0) return;

            Vector3D velocity = ship.GetShipVelocities().LinearVelocity;
            if (velocity.LengthSquared() < 0.25)
                velocity = ship.WorldMatrix.Forward;
            else
                velocity = Vector3D.Normalize(velocity);

            // ---------------------------------------------------------------
            // COMPUTE RING BASIS VECTORS — perpendicular to velocity
            //
            // We need two vectors (ringUp, ringRight) that together define the
            // plane of each ring, which must face the travel direction.
            //
            // Cross product method:
            //   1. Pick a world reference "up" (Y axis)
            //   2. If velocity is nearly parallel to Y, fall back to Z axis
            //   3. ringRight = normalize(velocity × refUp)
            //   4. ringUp    = normalize(ringRight × velocity)
            // ---------------------------------------------------------------
            Vector3D refUp = Vector3D.UnitY;
            if (Math.Abs(Vector3D.Dot(velocity, refUp)) > 0.99)
                refUp = Vector3D.UnitZ; // fallback when flying straight up/down

            Vector3D ringRight = Vector3D.Normalize(Vector3D.Cross(velocity, refUp));
            Vector3D ringUp    = Vector3D.Normalize(Vector3D.Cross(ringRight, velocity));

            Vector3D   origin = ship.WorldMatrix.Translation;
            MyStringId matId  = MyStringId.GetOrCompute(materialName);

            // Animation scroll: project world position onto velocity axis, mod spacing
            double projection  = Vector3D.Dot(origin, velocity);
            double scrollOffset = ringSpacing - (Math.Abs(projection) % ringSpacing);

            // Cap ring count to keep render budget sane
            int maxRings = (int)Math.Min(length / ringSpacing, 40);

            for (int i = 0; i < maxRings; i++)
            {
                double d = scrollOffset + i * ringSpacing;
                if (d > length) break;

                // Quadratic alpha fade: near = visible, far = near-invisible
                float t     = (float)(d / length);
                float alpha = baseAlpha * (1f - t * t);
                if (alpha < 0.004f) continue;

                Vector3D center = origin + velocity * d;
                Vector4 renderColor = color.ToVector4();
                renderColor.W = alpha;

                DrawFrame(center, ringUp, ringRight, renderColor, (float)halfWidth, (float)halfHeight, matId, thickness);
            }
        }

        /// <summary>
        /// Draws one rectangular ring at 'center'.
        /// Uses velocity-perpendicular basis vectors (ringUp, ringRight) — NOT ship orientation.
        /// This ensures rings always face the direction of travel.
        /// </summary>
        private static void DrawFrame(
            Vector3D center,
            Vector3D ringUp,
            Vector3D ringRight,
            Vector4 color,
            float halfWidth,
            float halfHeight,
            MyStringId matId,
            float thickness)
        {
            Vector3D up   = ringUp    * halfHeight;
            Vector3D left = ringRight * halfWidth;

            Vector3D tl = center + up + left;
            Vector3D tr = center + up - left;
            Vector3D bl = center - up + left;
            Vector3D br = center - up - left;

            MyTransparentGeometry.AddLineBillboard(matId, color, tl, (Vector3)(tr - tl), 1f, thickness);
            MyTransparentGeometry.AddLineBillboard(matId, color, tr, (Vector3)(br - tr), 1f, thickness);
            MyTransparentGeometry.AddLineBillboard(matId, color, br, (Vector3)(bl - br), 1f, thickness);
            MyTransparentGeometry.AddLineBillboard(matId, color, bl, (Vector3)(tl - bl), 1f, thickness);
        }
    }
}
