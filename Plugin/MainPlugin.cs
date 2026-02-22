// Plugin/MainPlugin.cs
using VRage.Plugins;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using Plugin.Services;
using Plugin.Models;

namespace Plugin
{
    public class MainPlugin : IPlugin
    {
        private ConfigService          _configService;
        private PhysicsService         _physics;
        private FlightComputerService  _flightComputer;
        private TelemetryService       _telemetry;
        private GpsManagerService      _gpsManager;
        private InputHandlerService    _inputHandler;
        private HudDisplayService      _hudDisplay;
        private AudioService           _audio;
        private TerminalControlService _terminalControls;

        private double _lastRange = -1;
        private bool   _initialized = false;

        public void Init(object gameInstance)
        {
            _configService = new ConfigService();
            _configService.Load();

            _physics          = new PhysicsService(_configService);
            _gpsManager       = new GpsManagerService(_configService.Data);
            _flightComputer   = new FlightComputerService(_physics, _configService);
            _telemetry        = new TelemetryService(_configService);
            _inputHandler     = new InputHandlerService(_configService.Data, _physics, _gpsManager);
            _hudDisplay       = new HudDisplayService(_configService);
            _audio            = new AudioService();
            _terminalControls = new TerminalControlService(_gpsManager, _configService);

            _initialized = true;
        }

        public void Update()
        {
            if (!_initialized || MyAPIGateway.Session == null) return;

            _terminalControls.Initialize();

            var ship = MyAPIGateway.Session.Player?.Controller?.ControlledEntity as IMyShipController;
            if (ship == null) return;

            // --- PHYSICS ---
            float maxDecel = _physics.CalculateMaxDeceleration(ship);

            // --- TELEMETRY ---
            // UpdatePlanetData takes liveMaxDecel to compute gravity sustainability
            double altitude = _telemetry.GetAltitude(ship);
            float  gravityG = _telemetry.GetGravityG(ship);
            _telemetry.UpdatePlanetData(ship, maxDecel);

            // --- INPUT ---
            _inputHandler.Update(ship, ref _lastRange);

            // --- FLIGHT COMPUTER ---
            // isWarning declared ONCE here (CS0128 guard)
            bool isWarning = _flightComputer.DrawBrakingTunnel(ship);

            // --- AUDIO ---
            _audio.PlayWarningSound(isWarning);

            // --- HUD ---
            var entity = ship.CubeGrid as VRage.Game.Entity.MyEntity;
            float mass = entity?.Physics?.Mass ?? 0f;

            _hudDisplay.Draw(
                mass, maxDecel, altitude, _lastRange,
                gravityG, isWarning,
                _telemetry.CurrentApproach); // pass planet approach data to HUD

            // --- AUTO SCAN (throttled ~2s) ---
            _gpsManager.ScanForVoxels(ship);
        }

        public void Dispose()
        {
            _terminalControls?.Terminate();
            _hudDisplay?.Hide();
            _configService?.Save();
        }
    }
}
