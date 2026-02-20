// Plugin/MainPlugin.cs
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
        private ConfigService _config;
        private PhysicsService _physics;
        private FlightComputerService _flightComputer;
        private TelemetryService _telemetry;
        private GpsManagerService _gpsManager;
        private InputHandlerService _inputHandler;
        private HudDisplayService _hudDisplay;

        // Shared data for the HUD
        private double _lastRange = -1;
        private double _lastAltitude = -1;

        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
        {
            _config = new ConfigService();
            _config.Load();

            _physics = new PhysicsService(_config);
            _flightComputer = new FlightComputerService(_physics, _config);
            _telemetry = new TelemetryService(_config);
            _gpsManager = new GpsManagerService(_config);
            // _inputHandler = new InputHandlerService(_config, _physics);
            _inputHandler = new InputHandlerService(_config, _physics, _gpsManager);
            _hudDisplay = new HudDisplayService(_config);
        }

        public override void UpdateBeforeSimulation()
        {
            if (MyAPIGateway.Session?.Player == null) return;

            var ship = MyAPIGateway.Session.Player.Controller?.ControlledEntity as IMyShipController;
            if (ship == null) return;

            // 1. Calculations
            _flightComputer.DrawBrakingTunnel(ship);
            _lastAltitude = _telemetry.GetAltitude(ship);
            _inputHandler.Update(ship, ref _lastRange); // Pass range back by ref

            // 2. Physics Data
            // float mass = (ship.CubeGrid as VRage.Game.Entity.MyEntity).Physics.Mass;
            var entity = ship.CubeGrid as VRage.Game.Entity.MyEntity;
            float mass = entity?.Physics?.Mass ?? 0f;
            float maxDecel = _physics.CalculateMaxDeceleration(ship);

            // 3. Render HUD
            _hudDisplay.Draw(mass, maxDecel, _lastAltitude, _lastRange);

            _gpsManager.ScanForVoxels(ship);
        }

        protected override void UnloadData()
        {
            _config?.Save();
            _config = null;
            _physics = null;
            _flightComputer = null;
            _telemetry = null;
            _gpsManager = null;
            _inputHandler = null;
            _hudDisplay = null;
        }
    }
}