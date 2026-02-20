// Plugin/Services/GpsManagerService.cs
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Plugin.Services
{
    public class GpsManagerService
    {
        private readonly ConfigService _config;

        public GpsManagerService(ConfigService config)
        {
            _config = config;
        }

        /// <summary>
        /// Scans for nearby voxels (asteroids/planets) within the configured survey radius.
        /// </summary>
        public void ScanForVoxels(IMyShipController ship)
        {
            float radius = _config.Data.SurveyRadius;
            HashSet<IMyEntity> entities = new HashSet<IMyEntity>();

            // Collect all voxel entities in the scene
            MyAPIGateway.Entities.GetEntities(entities, e => e is IMyVoxelBase);

            foreach (var entity in entities)
            {
                var voxel = entity as IMyVoxelBase;
                if (voxel == null) continue;

                // Future implementation: Check distance and iterate materials
            }
        }
    }
}