// Plugin/Services/PhysicsService.cs
using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using Sandbox.Game.Entities;
using VRage.Game.ModAPI;
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

        // -----------------------------------------------------------------------
        // BRAKING THRUST
        // -----------------------------------------------------------------------

        /// <summary>
        /// Sums thrust from blocks whose force direction OPPOSES the current velocity.
        /// SE thruster WorldMatrix.Forward = direction of thrust force (not exhaust).
        /// Positive dot(thruster.Forward, -velocityDir) means the thruster helps brake.
        /// </summary>
        public float CalculateMaxDeceleration(IMyShipController ship)
        {
            if (ship == null) return 0f;

            Vector3D velocityDir = ship.GetShipVelocities().LinearVelocity;
            if (velocityDir.LengthSquared() < 0.01)
                velocityDir = ship.WorldMatrix.Forward;
            else
                velocityDir = Vector3D.Normalize(velocityDir);

            Vector3D brakeDir = -velocityDir;

            float totalMass   = GetTotalMass(ship);
            float brakingThrust = 0f;

            var blocks = new List<IMySlimBlock>();
            ship.CubeGrid.GetBlocks(blocks, b => b.FatBlock is IMyThrust);

            foreach (var slim in blocks)
            {
                var thruster = slim.FatBlock as IMyThrust;
                if (thruster == null || !thruster.IsWorking) continue;

                double contribution = Vector3D.Dot(thruster.WorldMatrix.Forward, brakeDir);
                if (contribution > 0.01)
                    brakingThrust += thruster.MaxEffectiveThrust;
            }

            if (totalMass < 1f || brakingThrust < 1f) return 0f;
            return brakingThrust / totalMass;
        }

        public float CalculateStoppingDistance(IMyShipController ship)
        {
            if (ship == null) return 0f;
            float maxDecel = CalculateMaxDeceleration(ship);
            if (maxDecel < 0.01f) return 0f;
            double velocity = ship.GetShipSpeed();
            return (float)((velocity * velocity) / (2.0 * maxDecel));
        }

        // -----------------------------------------------------------------------
        // COLLISION DETECTION
        // -----------------------------------------------------------------------

        /// <summary>
        /// Returns the distance (m) to the nearest obstacle within 'maxDistance' meters
        /// along the velocity vector, or -1 if no obstacle found.
        ///
        /// MULTIPLE RAYS (5):
        ///   Center ray + 4 corner rays offset by ship's real half-extents.
        ///   Ship half-extents come from the combined bounding box of all connected
        ///   subgrids (pistons, rotors, connectors, magnetic plates).
        ///   Offsets use ship WorldMatrix.Up and Right — they rotate with the ship.
        ///
        /// OWN-GRID FILTERING:
        ///   GridGroups.GetGroup(GridLinkTypeEnum.Physical) returns all mechanically
        ///   linked grids. Hits on any of those grids are ignored.
        ///   Voxels and foreign grids are always treated as real obstacles.
        ///
        /// BACKWARD FLIGHT:
        ///   Rays follow velocity direction — correct for any flight direction.
        ///
        /// RETURNS: meters to nearest valid hit, or -1 if clear.
        /// </summary>
        public double NearestObstacleDistance(IMyShipController ship, double maxDistance)
        {
            if (ship == null || maxDistance <= 0) return -1;

            Vector3D velocity = ship.GetShipVelocities().LinearVelocity;
            if (velocity.LengthSquared() < 1.0) return -1;

            Vector3D velDir = Vector3D.Normalize(velocity);
            Vector3D origin = ship.CubeGrid.WorldAABB.Center;

            var ownIds = GetConnectedGridIds(ship.CubeGrid);

            // Use ship orientation for corner offsets (rotates with ship)
            Vector3D halfExtent = GetConnectedHalfExtent(ship.CubeGrid) + _config.Data.CollisionMargin;
            Vector3D shipRight  = ship.WorldMatrix.Right;
            Vector3D shipUp     = ship.WorldMatrix.Up;

            // Project ship axes perpendicular to velocity
            Vector3D axisRight = shipRight - Vector3D.Dot(shipRight, velDir) * velDir;
            Vector3D axisUp    = shipUp    - Vector3D.Dot(shipUp,    velDir) * velDir;
            if (axisRight.LengthSquared() > 0.001) axisRight = Vector3D.Normalize(axisRight);
            if (axisUp.LengthSquared()    > 0.001) axisUp    = Vector3D.Normalize(axisUp);

            double hw = halfExtent.X;  // right extent
            double hh = halfExtent.Y;  // up extent

            // 5 rays: center + 4 corners — same pattern as tunnel ring corners
            var offsets = new[]
            {
                Vector3D.Zero,
                 axisUp * hh + axisRight * hw,
                 axisUp * hh - axisRight * hw,
                -axisUp * hh + axisRight * hw,
                -axisUp * hh - axisRight * hw,
            };

            double nearest = -1;

            foreach (var offset in offsets)
            {
                Vector3D rayStart = origin + offset;
                Vector3D rayEnd   = rayStart + velDir * maxDistance;

                IHitInfo hit;
                if (!MyAPIGateway.Physics.CastRay(rayStart, rayEnd, out hit)) continue;

                var hitGrid = hit.HitEntity?.GetTopMostParent() as IMyCubeGrid;
                bool isOwn  = hitGrid != null && ownIds.Contains(hitGrid.EntityId);
                if (isOwn) continue;

                double dist = Vector3D.Distance(origin + offset, hit.Position);
                if (nearest < 0 || dist < nearest)
                    nearest = dist;
            }

            return nearest;
        }

        /// <summary>Convenience bool wrapper used by older callers.</summary>
        public bool IsCollisionImminent(IMyShipController ship, double distance)
            => NearestObstacleDistance(ship, distance) >= 0;

        // -----------------------------------------------------------------------
        // GRID SIZE / CONNECTED SUBGRIDS
        // -----------------------------------------------------------------------

        /// <summary>
        /// Returns entity IDs of the ship's main grid plus all mechanically
        /// connected subgrids (pistons, rotors, connectors, landing gear).
        /// </summary>
        public static HashSet<long> GetConnectedGridIds(IMyCubeGrid root)
        {
            var ids = new HashSet<long>();
            var grids = new List<IMyCubeGrid>();
            MyAPIGateway.GridGroups.GetGroup(root, GridLinkTypeEnum.Physical, grids);
            foreach (var g in grids) ids.Add(g.EntityId);
            return ids;
        }

        /// <summary>
        /// Returns the combined half-extents of the root grid and all connected
        /// subgrids, expressed in world space. Used to scale collision ray offsets.
        /// </summary>
        public static Vector3D GetConnectedHalfExtent(IMyCubeGrid root)
        {
            var grids = new List<IMyCubeGrid>();
            MyAPIGateway.GridGroups.GetGroup(root, GridLinkTypeEnum.Physical, grids);

            BoundingBoxD combinedAabb = BoundingBoxD.CreateInvalid();
            foreach (var g in grids)
                combinedAabb = combinedAabb.Include(g.WorldAABB);

            return combinedAabb.HalfExtents;
        }

        // -----------------------------------------------------------------------
        // MASS
        // -----------------------------------------------------------------------

        public float GetTotalMass(IMyShipController ship)
        {
            if (ship == null) return 0f;
            var grids = new List<IMyCubeGrid>();
            MyAPIGateway.GridGroups.GetGroup(ship.CubeGrid, GridLinkTypeEnum.Physical, grids);
            float total = 0f;
            foreach (var g in grids)
                total += (g as MyCubeGrid)?.GetCurrentMass() ?? 0f;
            return total > 0f ? total : ship.CalculateShipMass().TotalMass;
        }

        // -----------------------------------------------------------------------
        // LASER RANGEFINDER (unchanged)
        // -----------------------------------------------------------------------

        public bool CastLaserRay(IMyShipController ship, double maxRange, out IHitInfo hit, out double range)
        {
            hit   = null;
            range = -1;

            Vector3D start = ship.WorldMatrix.Translation + ship.WorldMatrix.Forward * 5.0;
            Vector3D end   = start + ship.WorldMatrix.Forward * maxRange;

            if (MyAPIGateway.Physics.CastRay(start, end, out hit))
            {
                range = Vector3D.Distance(start, hit.Position);
                return true;
            }
            return false;
        }
    }
}
