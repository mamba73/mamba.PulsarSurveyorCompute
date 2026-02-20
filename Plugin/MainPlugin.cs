using Plugin.Services;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;

namespace Plugin
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class MainPlugin : MySessionComponentBase
    {
        private PhysicsService _physics;
        private FlightComputerService _flightComputer;
        private TelemetryService _telemetry;
        private GpsManagerService _gpsManager;

        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
        {
            _physics = new PhysicsService();
            _flightComputer = new FlightComputerService(_physics);
            _telemetry = new TelemetryService();
            _gpsManager = new GpsManagerService();
        }

        public override void UpdateBeforeSimulation()
        {
            if (MyAPIGateway.Session?.Player == null) return;

            // Correct way to get the controlled ship in ModAPI
            var ship = MyAPIGateway.Session.Player.Controller?.ControlledEntity as IMyShipController;
            if (ship == null) return;

            _flightComputer.DrawBrakingTunnel(ship);
            _telemetry.UpdatePlanetData(ship);
        }

        protected override void UnloadData()
        {
            _physics = null;
            _flightComputer = null;
            _telemetry = null;
            _gpsManager = null;
        }
    }
}