using System.Collections.Generic;
using System.Linq;
using Plugin.Models;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace Plugin.Services
{
    public class GpsManagerService
    {
        private List<ResourceMarker> _cache = new List<ResourceMarker>();

        public void ProcessOreDetection(Vector3D pos, string oreName, long asteroidId)
        {
            var existing = _cache.FirstOrDefault(m =>
                m.EntityId == asteroidId &&
                Vector3D.Distance(m.Position, pos) < 500);

            if (existing != null)
            {
                if (!existing.Ores.Contains(oreName))
                {
                    existing.Ores += $", {oreName}";
                    UpdateGpsName(existing);
                }
            }
            else
            {
                CreateNewMarker(pos, oreName, asteroidId);
            }
        }

        private void CreateNewMarker(Vector3D pos, string ore, long id)
        {
            string name = $"[Sector] [{id}] [{ore}]";
            var gps = MyAPIGateway.Session.GPS.Create(name, "Pulsar Surveyor", pos, true);
            MyAPIGateway.Session.GPS.AddLocalGps(gps);

            _cache.Add(new ResourceMarker { EntityId = id, Position = pos, Ores = ore, GpsInstance = gps });
        }

        private void UpdateGpsName(ResourceMarker marker)
        {
            marker.GpsInstance.Name = $"[Sector] [{marker.EntityId}] [{marker.Ores}]";
            MyAPIGateway.Session.GPS.ModifyGps(MyAPIGateway.Session.Player.IdentityId, marker.GpsInstance);
        }
    }
}