using VRage.Game.ModAPI;
using VRageMath;

namespace Plugin.Models
{
    public class ResourceMarker
    {
        public long EntityId { get; set; }
        public Vector3D Position { get; set; }
        public string Ores { get; set; }
        public IMyGps GpsInstance { get; set; }
    }
}