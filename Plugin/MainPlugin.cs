// Plugin/MainPlugin.cs
using VRage.Plugins;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using Plugin.Services;
using Plugin.Config;
using Plugin.Models;
using Plugin.Utils;

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
        private ChatCommandService _chatCommands;

        private double _lastRange   = -1;
        private bool   _initialized = false;
        private bool _terminalInitialized = false;

        public void Init(object gameInstance)
        {
            // Init logger first — all subsequent code can use LoggerUtil
            LoggerUtil.Initialize(new PluginConfig().PluginVersion);

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
            _chatCommands     = new ChatCommandService(_gpsManager);

            _initialized = true;
        }

        public void Update()
        {
            if (!_initialized) return;

            // Load config on first tick with a valid session.
            // Safe here — MyAPIGateway.Session and Utilities are available.
            _configService.TryLoadOnce();

            if (MyAPIGateway.Session == null) return;

            _chatCommands.Register(); // Ensure chat commands are registered after session is ready. Safe to call multiple times due to internal flag.

            // Check if TerminalControls is ready and not already initialized
            if (!_terminalInitialized && MyAPIGateway.TerminalControls != null)
            {
                _terminalControls.Initialize(); // Called only once
                _terminalInitialized = true;   // Close gate after first call
                LoggerUtil.Info("Terminal controls successfully initialized.");
            }

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
            LoggerUtil.Shutdown();
            _terminalControls?.Terminate();
            _hudDisplay?.Hide();
            _configService?.Save();
            _chatCommands?.Unregister();
        }
    }
}
