using Sandbox.ModAPI;
using Sandbox.Game.Entities;
using VRage.Game.ModAPI;
using VRageMath;

namespace Plugin.Services
{
    public class TelemetryService
    {
        public void UpdatePlanetData(IMyShipController ship)
        {
            // Use MyGamePruningStructure to find planets in Plugin API
            var planet = MyGamePruningStructure.GetClosestPlanet(ship.GetPosition());
            if (planet == null) return;

            double distToCenter = Vector3D.Distance(ship.GetPosition(), planet.PositionComp.GetPosition());
            float avgRadius = planet.AverageRadius;

            if (distToCenter < avgRadius * 3)
            {
                // Explicitly cast double length to float
                float gravityG = (float)(ship.GetNaturalGravity().Length() / 9.81);
                // logic for HUD rendering goes here
            }
        }
    }
}