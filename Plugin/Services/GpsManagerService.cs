// Plugin/Services/GpsManagerService.cs
using System.Collections.Generic;
using Sandbox.ModAPI;
using Sandbox.Game.Entities;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Plugin.Services
{
    public class GpsManagerService
    {
        private readonly ConfigService _config;
        private readonly HashSet<Vector3I> _detectedLocations = new HashSet<Vector3I>();

        public GpsManagerService(ConfigService config)
        {
            _config = config;
        }

        public void ScanForVoxels(IMyShipController ship)
        {
            if (ship == null) return;

            float radius = _config.Data.SurveyRadius;
            Vector3D shipPos = ship.GetPosition();

            // Create the search sphere
            BoundingSphereD searchSphere = new BoundingSphereD(shipPos, radius);

            // Fix: Use GetTopMostEntitiesInSphere for ModAPI compatibility
            List<IMyEntity> entities = MyAPIGateway.Entities.GetTopMostEntitiesInSphere(ref searchSphere);

            foreach (var entity in entities)
            {
                // Cast to concrete MyVoxelBase to access material extension methods
                var voxel = entity as MyVoxelBase;

                // Ignore nulls and planets (planets are too large for this specific scan method)
                if (voxel == null || voxel is MyPlanet) continue;

                ScanVoxelDetails(voxel, shipPos);
            }
        }

        private void ScanVoxelDetails(MyVoxelBase voxel, Vector3D shipPos)
        {
            // VoxelBaseExtensions.GetMaterialAt requires a MyVoxelBase receiver
            Vector3D worldPos = shipPos;
            var material = voxel.GetMaterialAt(ref worldPos);

            if (material != null && !material.Id.SubtypeName.Contains("Stone"))
            {
                CreateTemporaryGps(material.Id.SubtypeName, shipPos);
            }
        }

        private void CreateTemporaryGps(string oreName, Vector3D position)
        {
            // Group detections into 100m sectors to prevent GPS marker spam
            Vector3I sector = new Vector3I(position / 100);
            if (_detectedLocations.Add(sector))
            {
                var gps = MyAPIGateway.Session.GPS.Create(
                    $"[Pulsar] {oreName}",
                    "Detected by Pulsar Surveyor",
                    position,
                    true,
                    true);

                MyAPIGateway.Session.GPS.AddLocalGps(gps);
            }
        }
    }
}