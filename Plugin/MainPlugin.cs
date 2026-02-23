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
        private GravityWellRenderer    _gravityWell;

        private double _lastRange   = -1;
        private bool   _initialized = false;

        public void Init(object gameInstance)
        {
            _configService    = new ConfigService();
            _configService.Load();

            _physics          = new PhysicsService(_configService);
            _gpsManager       = new GpsManagerService(_configService.Data);
            _flightComputer   = new FlightComputerService(_physics, _configService);
            _telemetry        = new TelemetryService(_configService);
            _inputHandler     = new InputHandlerService(_configService.Data, _physics, _gpsManager);
            _hudDisplay       = new HudDisplayService(_configService);
            _audio            = new AudioService();
            _terminalControls = new TerminalControlService(_gpsManager, _configService);
            _gravityWell      = new GravityWellRenderer(_configService);

            _initialized = true;
        }

        public void Update()
        {
            if (!_initialized || MyAPIGateway.Session == null) return;

            _terminalControls.Initialize();

            var ship = MyAPIGateway.Session.Player?.Controller?.ControlledEntity as IMyShipController;

            // No cockpit → hide HUD, still render gravity wells for external view
            if (ship == null)
            {
                _hudDisplay.Draw(null, 0, 0, -1, -1, 0, false, null);
                _gravityWell.Draw(null, _telemetry.NearbyPlanets);
                return;
            }

            // --- PHYSICS ---
            float mass     = _physics.GetTotalMass(ship);
            float maxDecel = _physics.CalculateMaxDeceleration(ship);

            // --- TELEMETRY (throttled planet refresh) ---
            _telemetry.UpdatePlanetData(ship, maxDecel);
            double altitude = _telemetry.GetAltitude(ship);
            float  gravityG = _telemetry.GetGravityG(ship);

            // --- INPUT ---
            _inputHandler.Update(ship, ref _lastRange);

            // --- FLIGHT COMPUTER (tunnel + collision) ---
            bool isWarning = _flightComputer.DrawBrakingTunnel(ship);

            // --- GRAVITY WELL VISUALIZATION ---
            _gravityWell.Draw(ship, _telemetry.NearbyPlanets);

            // --- AUDIO ---
            _audio.PlayWarningSound(isWarning);

            // --- HUD ---
            _hudDisplay.Draw(
                ship, mass, maxDecel, altitude, _lastRange,
                gravityG, isWarning, _telemetry.CurrentApproach);
        }

        public void Dispose()
        {
            _terminalControls?.Terminate();
            _hudDisplay?.Hide();
            _configService?.Save();
        }
    }
}
