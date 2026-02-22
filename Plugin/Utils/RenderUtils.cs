// Plugin/Utils/RenderUtils.cs
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace Plugin.Utils
{
    public static class RenderUtils
    {
        /// <summary>
        /// Renders a 3D rectangular tunnel along the ship's velocity vector.
        /// Frames (rings) are spaced 20m apart from origin to 'length' meters.
        /// Each ring is drawn as 4 line billboards forming a square cross-section.
        ///
        /// Fallback: if ship is nearly stationary, uses the forward vector instead of velocity.
        /// This prevents the tunnel from collapsing to a point at low speed.
        ///
        /// Color semantics (set by FlightComputerService):
        ///   Green  → clear path
        ///   Orange → caution zone (within stopping distance)
        ///   Red    → imminent collision (within 50% of stopping distance)
        /// </summary>
        public static void DrawTunnel(
            IMyShipController ship,
            double length,
            Color color,
            float alpha,
            float scale,
            string materialName,
            float thickness)
        {
            Vector3D velocity = ship.GetShipVelocities().LinearVelocity;

            if (velocity.LengthSquared() < 1)
                velocity = ship.WorldMatrix.Forward; // stationary fallback
            else
                velocity = Vector3D.Normalize(velocity);

            Vector3D   startPos = ship.WorldMatrix.Translation;
            MyStringId matId    = MyStringId.GetOrCompute(materialName);

            Vector4 renderColor = color.ToVector4();
            renderColor.W = alpha; // Apply configured transparency

            for (double d = 20; d <= length; d += 20)
            {
                Vector3D center = startPos + velocity * d;
                DrawFrame(center, ship.WorldMatrix, renderColor, scale, matId, thickness);
            }
        }

        /// <summary>
        /// Draws a single rectangular ring at 'center'.
        /// Corners are computed from the ship's world-space Up and Left axes.
        /// </summary>
        private static void DrawFrame(
            Vector3D center,
            MatrixD worldMatrix,
            Vector4 color,
            float scale,
            MyStringId matId,
            float thickness)
        {
            Vector3D up   = worldMatrix.Up   * scale;
            Vector3D left = worldMatrix.Left * scale;

            Vector3D tl = center + up + left;  // top-left corner
            Vector3D tr = center + up - left;  // top-right corner
            Vector3D bl = center - up + left;  // bottom-left corner
            Vector3D br = center - up - left;  // bottom-right corner

            // Four edges of the rectangular ring
            MyTransparentGeometry.AddLineBillboard(matId, color, tl, (Vector3)(tr - tl), 1f, thickness);
            MyTransparentGeometry.AddLineBillboard(matId, color, tr, (Vector3)(br - tr), 1f, thickness);
            MyTransparentGeometry.AddLineBillboard(matId, color, br, (Vector3)(bl - br), 1f, thickness);
            MyTransparentGeometry.AddLineBillboard(matId, color, bl, (Vector3)(tl - bl), 1f, thickness);
        }
    }
}
