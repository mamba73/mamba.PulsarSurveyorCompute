// Plugin/Models/ResourceMarker.cs
using VRage.Game.ModAPI;
using VRageMath;

namespace Plugin.Models
{
    /// <summary>
    /// Represents one tracked asteroid body in the survey cache.
    /// Keyed by the voxel entity's EntityId so each physical asteroid gets exactly one marker.
    /// The GPS marker position is the asteroid's geometric center (WorldAABB.Center),
    /// NOT the player's position at scan time.
    /// </summary>
    public class ResourceMarker
    {
        /// <summary>Voxel entity ID — primary cache key.</summary>
        public long EntityId { get; set; }

        /// <summary>
        /// Asteroid geometric center in world coordinates (WorldAABB.Center).
        /// This is where the GPS pin will be placed — at the rock, not at the ship.
        /// </summary>
        public Vector3D Position { get; set; }

        /// <summary>
        /// Comma-separated ore names detected on this asteroid.
        /// Grows as new ores are found: "Iron" → "Iron, Gold" → "Iron, Gold, Uranium".
        /// </summary>
        public string OreName { get; set; }

        /// <summary>Human-readable asteroid label, e.g. "S01 A03".</summary>
        public string Title { get; set; }

        /// <summary>Live GPS marker reference — used to remove/update without iterating the full list.</summary>
        public IMyGps Gps { get; set; }
    }
}
