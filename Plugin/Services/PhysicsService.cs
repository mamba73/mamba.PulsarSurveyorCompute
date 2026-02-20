// Plugin/Services/PhysicsService.cs
using Sandbox.ModAPI;
using Sandbox.Game.Entities;
using VRage.Game.ModAPI;
using VRage.Game.Entity;
using VRageMath;

namespace Plugin.Services
{
    public class PhysicsService
    {
        private readonly ConfigService _config;

        public PhysicsService(ConfigService config)
        {
            _config = config;
        }

        public float CalculateMaxDeceleration(IMyShipController ship)
        {
            if (ship == null || ship.CubeGrid == null) return 0f;

            var entity = ship.CubeGrid as MyEntity;
            if (entity?.Physics == null) return 0f;

            float mass = entity.Physics.Mass;
            float totalThrust = _config.Data.DefaultThrustForce;

            return mass > 0 ? totalThrust / mass : 0f;
        }

        public float CalculateStoppingDistance(IMyShipController ship)
        {
            if (ship == null) return 0f;

            double velocity = ship.GetShipSpeed();
            float maxDecel = CalculateMaxDeceleration(ship);

            if (maxDecel <= 0) return 0f;

            // Physics formula: s = v^2 / (2 * a)
            return (float)((velocity * velocity) / (2 * maxDecel));
        }

        public bool IsCollisionImminent(IMyShipController ship, double distance)
        {
            if (ship == null || distance <= 0) return false;

            Vector3D start = ship.GetPosition();
            Vector3D end = start + (ship.WorldMatrix.Forward * distance);

            IHitInfo hit;
            return MyAPIGateway.Physics.CastRay(start, end, out hit);
        }

        public double GetDistanceToSurface(IMyShipController ship, MyPlanet planet)
        {
            if (planet == null || ship == null) return -1;
            Vector3D pos = ship.GetPosition();
            Vector3D surfacePoint = planet.GetClosestSurfacePointGlobal(ref pos);
            return Vector3D.Distance(pos, surfacePoint);
        }

        public double RaycastDistance(IMyShipController ship, double maxRange = 50000)
        {
            if (ship == null) return -1;

            Vector3D start = ship.GetPosition();
            Vector3D direction = ship.WorldMatrix.Forward;
            Vector3D end = start + (direction * maxRange);

            IHitInfo hit;
            if (MyAPIGateway.Physics.CastRay(start, end, out hit))
            {
                return Vector3D.Distance(start, hit.Position);
            }

            return -1;
        }
    }
}