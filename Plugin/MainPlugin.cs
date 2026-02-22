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
        private ConfigService         _configService;
        private PhysicsService        _physics;
        private FlightComputerService _flightComputer;
        private TelemetryService      _telemetry;
        private GpsManagerService     _gpsManager;
        private InputHandlerService   _inputHandler;
        private HudDisplayService     _hudDisplay;
        private AudioService          _audio;
        private TerminalControlService _terminalControls;

        // Persistent session state
        private double _lastRange = -1;
        private bool   _initialized = false;

        public void Init(object gameInstance)
        {
            _configService = new ConfigService();
            _configService.Load();

            // Initialization order matters:
            //   1. ConfigService first (all others depend on it)
            //   2. PhysicsService before FlightComputer (FlightComputer calls Physics)
            //   3. GpsManager before InputHandler (InputHandler reports to GpsManager)
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

            // Terminal controls are registered lazily on the first tick where the API is ready.
            // Calling Initialize() every tick is safe — it exits immediately after first success.
            _terminalControls.Initialize();

            var ship = MyAPIGateway.Session.Player?.Controller?.ControlledEntity as IMyShipController;
            if (ship == null) return;

            // --- TELEMETRY ---
            double altitude = _telemetry.GetAltitude(ship);
            float  gravityG = _telemetry.GetGravityG(ship);
            _telemetry.UpdatePlanetData(ship); // triggers low-altitude warning if applicable

            // --- INPUT (Laser / Shift+T reset) ---
            _inputHandler.Update(ship, ref _lastRange);

            // --- FLIGHT COMPUTER ---
            // Returns true when a collision is detected within the braking path.
            // FIX (CS0128): isWarning declared exactly ONCE here, used by both Audio and HUD.
            bool isWarning = _flightComputer.DrawBrakingTunnel(ship);

            // --- AUDIO ---
            _audio.PlayWarningSound(isWarning);

            // --- HUD ---
            var entity   = ship.CubeGrid as VRage.Game.Entity.MyEntity;
            float mass   = entity?.Physics?.Mass ?? 0f;
            float maxDecel = _physics.CalculateMaxDeceleration(ship); // uses live thrust

            _hudDisplay.Draw(mass, maxDecel, altitude, _lastRange, gravityG, isWarning);

            // --- AUTO ORE SCAN (throttled internally to ~2s) ---
            _gpsManager.ScanForVoxels(ship);
        }

        public void Dispose()
        {
            _terminalControls?.Terminate(); // Unregister terminal hook
            _hudDisplay?.Hide();            // Remove persistent HUD notification
            _configService?.Save();         // Persist any runtime config changes
        }
    }
}
