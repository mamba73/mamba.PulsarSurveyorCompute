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
        /// Casts multiple rays in a cross pattern to check for obstacles within
        /// 'distance' meters along the velocity vector.
        ///
        /// WHY MULTIPLE RAYS:
        ///   A single center ray misses objects to the sides of the ship.
        ///   A ship traveling laterally can collide with its flank, not its nose.
        ///   We cast 5 rays: center + 4 corner offsets based on ship half-extents.
        ///
        /// GRID FILTERING:
        ///   Hits on the ship's own grid (or physically connected subgrids) are ignored.
        ///   This prevents false positives from the ship's own geometry.
        ///   Also skips hits on grids connected via connectors / landing gear (subgridIds).
        ///
        /// CONNECTED SUBGRID RESOLUTION:
        ///   GetPhysicalConnections() returns all mechanically connected grids.
        ///   Connector and magnetic plate attachments are included.
        ///   The hit is valid only if the hit entity is NOT in the connected set.
        ///
        /// BACKWARD FLIGHT:
        ///   Direction is always along velocity, so backward flight is handled
        ///   correctly — rays go backward, ship's nose (forward face) is irrelevant.
        ///
        /// MARGIN:
        ///   Config.CollisionMargin (default 3m) is added to the half-extents as
        ///   maneuvering clearance.
        /// </summary>
        public bool IsCollisionImminent(IMyShipController ship, double distance)
        {
            if (ship == null || distance <= 0) return false;

            Vector3D velocity = ship.GetShipVelocities().LinearVelocity;
            if (velocity.LengthSquared() < 1.0) return false; // < 1 m/s — ignore

            Vector3D velDir = Vector3D.Normalize(velocity);
            Vector3D origin = ship.CubeGrid.WorldAABB.Center;

            // Collect IDs of own grid + all mechanically connected grids
            var ownIds = GetConnectedGridIds(ship.CubeGrid);

            // Half-extents of the combined grid bounding box
            Vector3D halfExtent = GetConnectedHalfExtent(ship.CubeGrid) + _config.Data.CollisionMargin;

            // Build perpendicular axes to velocity for corner offsets
            Vector3D up    = Vector3D.CalculatePerpendicularVector(velDir);
            Vector3D right = Vector3D.Cross(velDir, up);
            up    = Vector3D.Normalize(up);
            right = Vector3D.Normalize(right);

            // Half-width and half-height for offset
            double hw = Math.Max(halfExtent.X, halfExtent.Z);
            double hh = halfExtent.Y;

            // 5 rays: center + 4 corners
            var offsets = new[]
            {
                Vector3D.Zero,
                up * hh + right * hw,
                up * hh - right * hw,
               -up * hh + right * hw,
               -up * hh - right * hw,
            };

            Vector3D end = origin + velDir * distance;

            foreach (var offset in offsets)
            {
                Vector3D rayStart = origin + offset;
                Vector3D rayEnd   = end   + offset;

                IHitInfo hit;
                if (!MyAPIGateway.Physics.CastRay(rayStart, rayEnd, out hit)) continue;

                // Check hit entity is not own grid / subgrid
                var hitGrid = hit.HitEntity?.GetTopMostParent() as IMyCubeGrid;
                if (hitGrid == null)
                {
                    // Voxel or other non-grid entity — always a real obstacle
                    return true;
                }

                if (!ownIds.Contains(hitGrid.EntityId))
                    return true;
            }

            return false;
        }

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
