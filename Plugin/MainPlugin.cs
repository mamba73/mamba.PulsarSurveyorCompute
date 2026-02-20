// Plugin/MainPlugin.cs
using Plugin.Services;
using Sandbox.ModAPI;
using VRage.Game; // Required for MyObjectBuilder_SessionComponent
using VRage.Game.Components;
using VRage.Game.ModAPI;

namespace Plugin
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class MainPlugin : MySessionComponentBase
    {
        private ConfigService _config;
        private PhysicsService _physics;
        private FlightComputerService _flightComputer;
        private TelemetryService _telemetry;
        private GpsManagerService _gpsManager;
        private InputHandlerService _inputHandler;

        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
        {
            // Initialize configuration first to provide data to other services
            _config = new ConfigService();
            _config.Load();

            // Dependency Injection
            _physics = new PhysicsService(_config);
            _flightComputer = new FlightComputerService(_physics, _config);
            _telemetry = new TelemetryService(_config);
            _gpsManager = new GpsManagerService(_config);
            _inputHandler = new InputHandlerService(_config, _physics);
        }

        public override void UpdateBeforeSimulation()
        {
            // Guard clause to ensure player is valid and controlled entity is a ship
            if (MyAPIGateway.Session?.Player == null) return;

            var ship = MyAPIGateway.Session.Player.Controller?.ControlledEntity as IMyShipController;
            if (ship == null) return;

            // Delegate updates to specialized services
            _flightComputer.DrawBrakingTunnel(ship);
            _telemetry.UpdatePlanetData(ship);
            _inputHandler.Update(ship);
            _gpsManager.ScanForVoxels(ship);
        }

        protected override void UnloadData()
        {
            // Cleanup and persist settings
            _config?.Save();

            _config = null;
            _physics = null;
            _flightComputer = null;
            _telemetry = null;
            _gpsManager = null;
            _inputHandler = null;
        }
    }
}