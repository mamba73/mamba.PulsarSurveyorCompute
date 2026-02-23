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
        /// Calculates available braking thrust by summing working thruster blocks
        /// whose force OPPOSES the current velocity direction.
        ///
        /// SE THRUSTER CONVENTION:
        ///   In Space Engineers, thruster.WorldMatrix.Forward is the direction of
        ///   THRUST FORCE (the direction the ship is pushed), NOT the exhaust direction.
        ///   Example: rear thruster pushes ship FORWARD → WorldMatrix.Forward = ship.Forward.
        ///
        /// BRAKING LOGIC:
        ///   Braking force = thrust that opposes velocity.
        ///   If velocity = Forward, braking thrusters are those with Forward ≈ -velocity.
        ///   Dot product: contribution = dot(thruster.Forward, -velocityDir)
        ///   Positive contribution → thruster helps slow down.
        ///
        /// WHY PREVIOUS VERSION WAS WRONG:
        ///   Old code: dot(thruster.Forward, +velocityDir) > 0 → summed ACCELERATING thrusters.
        ///   This gave wrong stopping distance AND caused backward movement to always appear red
        ///   (forward thrusters were counted as "braking" backward motion).
        ///
        /// FALLBACK: Config.DefaultThrustForce when no braking thrusters are found.
        /// </summary>
        public float CalculateLiveThrustForce(IMyShipController ship)
        {
            if (ship == null) return _config.Data.DefaultThrustForce;

            var thrusters = new List<IMyTerminalBlock>();
            MyAPIGateway.TerminalActionsHelper
                .GetTerminalSystemForGrid(ship.CubeGrid)
                .GetBlocksOfType<IMyThrust>(thrusters);

            if (thrusters.Count == 0) return _config.Data.DefaultThrustForce;

            Vector3D velocityDir = ship.GetShipVelocities().LinearVelocity;
            if (velocityDir.LengthSquared() < 0.01)
                velocityDir = ship.WorldMatrix.Forward;
            else
                velocityDir = Vector3D.Normalize(velocityDir);

            // Braking direction is OPPOSITE to velocity
            Vector3D brakeDir = -velocityDir;

            float brakingThrust = 0f;
            foreach (var block in thrusters)
            {
                var thruster = block as IMyThrust;
                if (thruster == null || !thruster.IsWorking) continue;

                // FIX: dot against brakeDir (opposite to velocity), not velocityDir
                // Positive result = this thruster pushes against our motion = braking
                double contribution = Vector3D.Dot(thruster.WorldMatrix.Forward, brakeDir);
                if (contribution > 0)
                    brakingThrust += thruster.MaxEffectiveThrust * (float)contribution;
            }

            return brakingThrust > 0 ? brakingThrust : _config.Data.DefaultThrustForce;
        }

        /// <summary>
        /// Returns total ship mass in kg — matches the value shown in the SE HUD.
        ///
        /// WHY NOT entity.Physics.Mass:
        ///   Physics.Mass can return structural mass only (without cargo/inventory).
        ///   CalculateShipMass() includes everything: structure, cargo, fuel, players.
        ///   This matches what the SE native HUD displays.
        /// </summary>
        public float GetTotalMass(IMyShipController ship)
        {
            if (ship == null) return 0f;
            // CalculateShipMass returns the full mass including cargo/inventory
            var massInfo = ship.CalculateShipMass();
            return massInfo.TotalMass;
        }

        /// <summary>Max deceleration (m/s²) = live braking force / total mass.</summary>
        public float CalculateMaxDeceleration(IMyShipController ship)
        {
            if (ship == null || ship.CubeGrid == null) return 0f;
            float mass   = GetTotalMass(ship);
            float thrust = CalculateLiveThrustForce(ship);
            return mass > 0 ? thrust / mass : 0f;
        }

        /// <summary>Minimum stopping distance: s = v² / (2a).</summary>
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
        /// Fallback to forward vector if nearly stationary.
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
        /// Laser rangefinder raycast along the ship's forward axis.
        /// Range from Config.LaserMaxRange — not hardcoded.
        /// Offset 5m forward to clear own collision mesh.
        /// Returns distance to hit, or -1 if clear.
        /// </summary>
        public double RaycastDistance(IMyShipController ship)
        {
            if (ship == null) return -1;
            double maxRange = _config.Data.LaserMaxRange;
            Vector3D start  = ship.WorldMatrix.Translation + ship.WorldMatrix.Forward * 5.0;
            Vector3D end    = start + ship.WorldMatrix.Forward * maxRange;
            IHitInfo hit;
            if (MyAPIGateway.Physics.CastRay(start, end, out hit))
                return Vector3D.Distance(start, hit.Position);
            return -1;
        }

        public double GetDistanceToSurface(IMyShipController ship, MyPlanet planet)
        {
            if (planet == null || ship == null) return -1;
            Vector3D pos     = ship.GetPosition();
            Vector3D surface = planet.GetClosestSurfacePointGlobal(ref pos);
            return Vector3D.Distance(pos, surface);
        }
    }
}
