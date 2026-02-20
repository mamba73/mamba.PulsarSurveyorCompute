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
        public static void DrawTunnel(IMyShipController ship, double length, Color color, float alpha, float scale, string materialName, float thickness)
        {
            MatrixD worldMatrix = ship.WorldMatrix;
            Vector3D startPos = worldMatrix.Translation;
            Vector3D forward = worldMatrix.Forward;
            MyStringId materialId = MyStringId.GetOrCompute(materialName);

            Vector4 renderColor = color.ToVector4();
            renderColor.W = alpha;

            // Step every 20m as per logic
            for (double d = 20; d <= length; d += 20)
            {
                Vector3D center = startPos + (forward * d);
                DrawFrame(center, worldMatrix, renderColor, scale, materialId, thickness);
            }
        }

        private static void DrawFrame(Vector3D center, MatrixD worldMatrix, Vector4 color, float scale, MyStringId matId, float thickness)
        {
            Vector3D up = worldMatrix.Up * scale;
            Vector3D left = worldMatrix.Left * scale;

            Vector3D tl = center + up + left;
            Vector3D tr = center + up - left;
            Vector3D bl = center - up + left;
            Vector3D br = center - up - left;

            DrawLine(tl, tr, color, matId, thickness);
            DrawLine(tr, br, color, matId, thickness);
            DrawLine(br, bl, color, matId, thickness);
            DrawLine(bl, tl, color, matId, thickness);
        }

        private static void DrawLine(Vector3D start, Vector3D end, Vector4 color, MyStringId matId, float thickness)
        {
            // In many SE versions, BlendType is optional or part of MyBillboard
            // We use the most stable overload for line billboards
            MyTransparentGeometry.AddLineBillboard(
                matId,
                color,
                start,
                (Vector3)(end - start),
                1f,
                thickness);
        }
    }
}