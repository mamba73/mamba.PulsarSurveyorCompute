// Plugin/Services/PhysicsService.cs
using System.Collections.Generic;
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

        /// <summary>
        /// Calculates available braking thrust by summing MaxEffectiveThrust from all
        /// working thruster blocks whose force vector opposes the current velocity.
        ///
        /// WHY live thrust?
        ///   The braking tunnel length = v² / (2a). If the pilot adds 6 extra rear
        ///   thrusters in flight, the tunnel must shorten to reflect the stronger braking.
        ///   A hardcoded DefaultThrustForce would never react to this change.
        ///
        /// FALLBACK:
        ///   If no working thrusters are found (e.g., grid damage or script conflict),
        ///   Config.DefaultThrustForce is used so the tunnel still renders.
        /// </summary>
        public float CalculateLiveThrustForce(IMyShipController ship)
        {
            if (ship == null) return _config.Data.DefaultThrustForce;

            // Collect all thruster blocks on the ship's grid
            var thrusters = new List<IMyTerminalBlock>();
            MyAPIGateway.TerminalActionsHelper
                .GetTerminalSystemForGrid(ship.CubeGrid)
                .GetBlocksOfType<IMyThrust>(thrusters);

            if (thrusters.Count == 0)
                return _config.Data.DefaultThrustForce; // no thrusters found — use config fallback

            // Determine braking direction: opposite to current velocity.
            // If nearly stationary, use ship's forward vector as fallback.
            Vector3D velocityDir = ship.GetShipVelocities().LinearVelocity;
            if (velocityDir.LengthSquared() > 0.01)
                velocityDir = Vector3D.Normalize(velocityDir);
            else
                velocityDir = ship.WorldMatrix.Forward;

            float brakingThrust = 0f;
            foreach (var block in thrusters)
            {
                var thruster = block as IMyThrust;
                if (thruster == null || !thruster.IsWorking) continue;

                // thruster.WorldMatrix.Forward = direction the exhaust exits (world space)
                // The actual thrust force acts in the OPPOSITE direction of exhaust
                // Dot against -velocityDir tells us how well this thruster brakes
                double contribution = Vector3D.Dot(thruster.WorldMatrix.Forward, velocityDir);

                // contribution > 0 means: thruster exhaust points along velocity = thrust opposes velocity
                if (contribution > 0)
                    brakingThrust += thruster.MaxEffectiveThrust * (float)contribution;
            }

            // If no thruster contributes braking force in current direction, use config fallback
            return brakingThrust > 0 ? brakingThrust : _config.Data.DefaultThrustForce;
        }

        /// <summary>
        /// Calculates max deceleration (m/s²) = F / m.
        /// Uses live thrust force from actual thruster blocks.
        /// </summary>
        public float CalculateMaxDeceleration(IMyShipController ship)
        {
            if (ship == null || ship.CubeGrid == null) return 0f;

            var entity = ship.CubeGrid as MyEntity;
            if (entity?.Physics == null) return 0f;

            float mass        = entity.Physics.Mass;
            float totalThrust = CalculateLiveThrustForce(ship);

            return mass > 0 ? totalThrust / mass : 0f;
        }

        /// <summary>
        /// Calculates minimum stopping distance in meters using: s = v² / (2a).
        /// Returns 0 when deceleration cannot be determined.
        /// </summary>
        public float CalculateStoppingDistance(IMyShipController ship)
        {
            if (ship == null) return 0f;

            double velocity = ship.GetShipSpeed();
            float maxDecel  = CalculateMaxDeceleration(ship);

            if (maxDecel <= 0) return 0f;

            return (float)((velocity * velocity) / (2.0 * maxDecel));
        }

        /// <summary>
        /// Returns true if there is a solid obstacle within 'distance' meters along the velocity vector.
        /// Fallback: if speed is near zero, checks along the ship's forward axis.
        /// </summary>
        public bool IsCollisionImminent(IMyShipController ship, double distance)
        {
            if (ship == null || distance <= 0) return false;

            Vector3D velocity = ship.GetShipVelocities().LinearVelocity;
            if (velocity.LengthSquared() < 1) return false;

            Vector3D start     = ship.WorldMatrix.Translation;
            Vector3D direction = Vector3D.Normalize(velocity);
            Vector3D end       = start + direction * distance;

            IHitInfo hit;
            return MyAPIGateway.Physics.CastRay(start, end, out hit);
        }

        /// <summary>
        /// Fires a raycast from the ship's nose along its forward vector.
        /// LaserMaxRange is read from config — NOT hardcoded.
        /// Returns distance to first obstacle, or -1 if path is clear.
        /// </summary>
        public double RaycastDistance(IMyShipController ship)
        {
            if (ship == null) return -1;

            // FIX: maxRange now comes from config, not hardcoded at call site
            double maxRange = _config.Data.LaserMaxRange;

            // Offset 2.5m forward to avoid self-hit on the ship's own collision mesh
            Vector3D start     = ship.WorldMatrix.Translation + ship.WorldMatrix.Forward * 2.5;
            Vector3D direction = ship.WorldMatrix.Forward;
            Vector3D end       = start + direction * maxRange;

            IHitInfo hit;
            if (MyAPIGateway.Physics.CastRay(start, end, out hit))
                return Vector3D.Distance(start, hit.Position);

            return -1;
        }

        /// <summary>
        /// Returns the true terrain altitude above the closest planet, or -1 in space.
        /// </summary>
        public double GetDistanceToSurface(IMyShipController ship, MyPlanet planet)
        {
            if (planet == null || ship == null) return -1;
            Vector3D pos     = ship.GetPosition();
            Vector3D surface = planet.GetClosestSurfacePointGlobal(ref pos);
            return Vector3D.Distance(pos, surface);
        }
    }
}
