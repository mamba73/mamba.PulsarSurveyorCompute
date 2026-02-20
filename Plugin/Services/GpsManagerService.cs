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
        private readonly Dictionary<Vector3I, IMyGps> _sectorGpsMap = new Dictionary<Vector3I, IMyGps>();

        public GpsManagerService(ConfigService config)
        {
            _config = config;
        }

        public void ScanForVoxels(IMyShipController ship)
        {
            if (ship == null) return;

            float radius = _config.Data.SurveyRadius;
            Vector3D shipPos = ship.GetPosition();
            BoundingSphereD searchSphere = new BoundingSphereD(shipPos, radius);

            List<IMyEntity> entities = MyAPIGateway.Entities.GetTopMostEntitiesInSphere(ref searchSphere);

            foreach (var entity in entities)
            {
                var voxel = entity as MyVoxelBase;
                if (voxel == null || voxel is MyPlanet) continue;

                ScanVoxelDetails(voxel, shipPos);
            }
        }

        private void ScanVoxelDetails(MyVoxelBase voxel, Vector3D shipPos)
        {
            Vector3D worldPos = shipPos;
            var material = voxel.GetMaterialAt(ref worldPos);

            if (material != null && !material.Id.SubtypeName.Contains("Stone"))
            {
                UpdateOrCreateInfo(material.Id.SubtypeName, shipPos);
            }
        }

        private void UpdateOrCreateInfo(string oreName, Vector3D position)
        {
            Vector3I sector = new Vector3I(position / 200);

            if (_sectorGpsMap.ContainsKey(sector))
            {
                var existingGps = _sectorGpsMap[sector];

                if (!existingGps.Name.Contains(oreName))
                {
                    // Update the local object
                    existingGps.Name += $", {oreName}";

                    // Force HUD refresh: Remove and re-add is the most reliable ModAPI method
                    MyAPIGateway.Session.GPS.RemoveLocalGps(existingGps.Hash);
                    MyAPIGateway.Session.GPS.AddLocalGps(existingGps);
                }
            }
            else
            {
                var newGps = MyAPIGateway.Session.GPS.Create(
                    $"[Pulsar] {oreName}",
                    "Pulsar Composite Scan",
                    position,
                    true,
                    true);

                MyAPIGateway.Session.GPS.AddLocalGps(newGps);
                _sectorGpsMap.Add(sector, newGps);
            }
        }

        public void ClearScanData()
        {
            // Remove markers from the game world first
            foreach (var gps in _sectorGpsMap.Values)
            {
                MyAPIGateway.Session.GPS.RemoveLocalGps(gps.Hash);
            }

            _sectorGpsMap.Clear();
            MyAPIGateway.Utilities.ShowNotification("Pulsar Scan Data Cleared", 2000, MyFontEnum.Green);
        }
    }
}