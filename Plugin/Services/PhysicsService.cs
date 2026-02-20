using Sandbox.ModAPI;
using Sandbox.Game.Entities;
using VRage.Game.ModAPI;
using VRage.Game.Entity; // Added for MyEntity cast
using VRageMath;

namespace Plugin.Services
{
    public class PhysicsService
    {
        public float CalculateMaxDeceleration(IMyShipController ship)
        {
            if (ship == null || ship.CubeGrid == null) return 0f;

            // Cast IMyCubeGrid to MyEntity to access the physics body mass
            var entity = ship.CubeGrid as MyEntity;
            if (entity?.Physics == null) return 0f;

            float mass = entity.Physics.Mass;

            // Default thrust value for now
            float thrust = 1000000f;

            return mass > 0 ? thrust / mass : 0f;
        }

        public bool IsCollisionImminent(IMyShipController ship, double distance)
        {
            if (ship == null) return false;

            Vector3D forward = ship.WorldMatrix.Forward;
            Vector3D start = ship.GetPosition();
            Vector3D end = start + (forward * distance);

            IHitInfo hit;
            // Raycast logic to detect voxels or other grids
            return MyAPIGateway.Physics.CastRay(start, end, out hit);
        }

        public double GetDistanceToSurface(IMyShipController ship, MyPlanet planet)
        {
            if (planet == null || ship == null) return -1;

            Vector3D pos = ship.GetPosition();
            Vector3D surfacePoint = planet.GetClosestSurfacePointGlobal(ref pos);
            return Vector3D.Distance(pos, surfacePoint);
        }
    }
}