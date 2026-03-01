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
        private AsteroidFullScanService _asteroidScanner;

        private double _lastRange   = -1;
        private bool   _initialized = false;

        public void Init(object gameInstance)
        {
            _configService    = new ConfigService();

            _physics          = new PhysicsService(_configService);
            _gpsManager       = new GpsManagerService(_configService.Data);
            _flightComputer   = new FlightComputerService(_physics, _configService);
            _telemetry        = new TelemetryService(_configService);
            _asteroidScanner  = new AsteroidFullScanService(_gpsManager, _configService);
            _inputHandler     = new InputHandlerService(_configService.Data, _configService, _physics, _gpsManager, _asteroidScanner);
            _hudDisplay       = new HudDisplayService(_configService);
            _audio            = new AudioService();
            _terminalControls = new TerminalControlService(_gpsManager, _configService);
            _gravityWell      = new GravityWellRenderer(_configService);

            _initialized = true;
        }

        public void Update()
        {
            if (!_initialized) return;

            // Load config on first tick with a valid session.
            // Safe here — MyAPIGateway.Session and Utilities are available.
            _configService.TryLoadOnce();

            if (MyAPIGateway.Session == null) return;

            _terminalControls.Initialize();

            var ship = MyAPIGateway.Session.Player?.Controller?.ControlledEntity as IMyShipController;

            // Treat static-grid controllers (desks, beds, decorative seats on stations)
            // the same as "not in a cockpit" — no flight data, hide HUD.
            if (ship == null || ship.CubeGrid.IsStatic)
            {
                _hudDisplay.Draw(null, 0, 0, -1, -1, 0, false, -1, null);
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
            double collisionDist = _flightComputer.DrawBrakingTunnel(ship);
            bool   isWarning     = collisionDist >= 0;

            // --- GRAVITY WELL VISUALIZATION ---
            _gravityWell.Draw(ship, _telemetry.NearbyPlanets);

            // --- AUDIO ---
            _audio.PlayWarningSound(isWarning);

            // --- HUD ---
            _hudDisplay.Draw(
                ship, mass, maxDecel, altitude, _lastRange,
                gravityG, isWarning, collisionDist, _telemetry.CurrentApproach);
        }

        public void Dispose()
        {
            _terminalControls?.Terminate();
            _hudDisplay?.Hide();
            _configService?.Save();
        }
    }
}
