// Plugin/Services/ConfigService.cs
using System;
using System.IO;
using System.Xml.Serialization;
using Plugin.Models;
using Sandbox.ModAPI;
using VRage.Utils;

namespace Plugin.Services
{
    /// <summary>
    /// Manages persistent config.xml in two storage locations:
    ///
    ///   1. SESSION LocalStorage (MyAPIGateway.Utilities) — preferred, world-specific.
    ///      Available only after a session is active (MyAPIGateway.Session != null).
    ///
    ///   2. FILESYSTEM fallback — %AppData%\SpaceEngineers\Storage\PulsarSurveyorCompute\
    ///      Used when no session is active (e.g. called from IPlugin.Init()).
    ///      IPlugin.Init() fires before any world loads; MyAPIGateway.Utilities is
    ///      session-bound and throws NullReferenceException if called from Init().
    ///
    /// LOAD SEQUENCE (in MainPlugin):
    ///   Init()   → _configService.Data is set to defaults only (no file I/O)
    ///   Update() → _configService.TryLoadOnce() called on first tick with valid session
    ///              → reads from LocalStorage (session path available at that point)
    ///   Dispose() → Save() writes back any runtime changes
    /// </summary>
    public class ConfigService
    {
        private const string ConfigFileName    = "config.xml";
        private const string FallbackDirectory = "PulsarSurveyorCompute";

        private bool _loaded = false;

        /// <summary>
        /// The active configuration. Non-null from construction.
        /// Populated with default values until TryLoadOnce() succeeds.
        /// </summary>
        public Config Data { get; private set; } = new Config();

        // -----------------------------------------------------------------------
        // PRIMARY INTERFACE
        // -----------------------------------------------------------------------

        /// <summary>
        /// Called from MainPlugin.Update() on every tick until loading succeeds.
        /// Reads from LocalStorage (requires active session).
        /// No-op once loaded successfully.
        /// </summary>
        public void TryLoadOnce()
        {
            if (_loaded) return;
            if (MyAPIGateway.Session == null) return; // session not ready yet
            Load();
        }

        /// <summary>
        /// Read config from LocalStorage. Requires active session.
        /// Falls back to filesystem if LocalStorage throws.
        /// Falls back to defaults if both sources fail.
        /// </summary>
        public void Load()
        {
            // Guard: if session is null, LocalStorage will throw NullReferenceException
            if (MyAPIGateway.Session == null)
            {
                TryLoadFromFilesystem();
                return;
            }

            try
            {
                if (MyAPIGateway.Utilities.FileExistsInLocalStorage(ConfigFileName, typeof(Config)))
                {
                    using (var reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(ConfigFileName, typeof(Config)))
                    {
                        var ser = new XmlSerializer(typeof(Config));
                        Data = (Config)ser.Deserialize(reader);
                    }
                }
                else
                {
                    Data = new Config(); // first run — write defaults
                    Save();
                }
                _loaded = true;
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[Pulsar] Config Load (LocalStorage) Error: {ex.Message}");
                TryLoadFromFilesystem();
                _loaded = true; // don't retry on every tick after failure
            }
        }

        /// <summary>
        /// Write config to LocalStorage. Requires active session.
        /// Falls back to filesystem if session is unavailable.
        /// </summary>
        public void Save()
        {
            if (MyAPIGateway.Session == null)
            {
                TryWriteToFilesystem();
                return;
            }

            try
            {
                using (var writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(ConfigFileName, typeof(Config)))
                {
                    var ser = new XmlSerializer(typeof(Config));
                    ser.Serialize(writer, Data);
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[Pulsar] Config Save (LocalStorage) Error: {ex.Message}");
                TryWriteToFilesystem();
            }
        }

        // -----------------------------------------------------------------------
        // FILESYSTEM FALLBACK
        // Used when session is not active (e.g. called from Init, or session error)
        // -----------------------------------------------------------------------

        private void TryLoadFromFilesystem()
        {
            try
            {
                string path = GetFallbackPath();
                if (File.Exists(path))
                {
                    using (var reader = new StreamReader(path))
                    {
                        var ser = new XmlSerializer(typeof(Config));
                        Data = (Config)ser.Deserialize(reader);
                    }
                    MyLog.Default.WriteLineAndConsole("[Pulsar] Config loaded from filesystem fallback.");
                }
                // If fallback also missing: keep defaults silently
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[Pulsar] Config Load (filesystem) Error: {ex.Message}");
                Data = new Config();
            }
        }

        private void TryWriteToFilesystem()
        {
            try
            {
                string path = GetFallbackPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (var writer = new StreamWriter(path))
                {
                    var ser = new XmlSerializer(typeof(Config));
                    ser.Serialize(writer, Data);
                }
                MyLog.Default.WriteLineAndConsole("[Pulsar] Config saved to filesystem fallback.");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[Pulsar] Config Save (filesystem) Error: {ex.Message}");
            }
        }

        private static string GetFallbackPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "SpaceEngineers", "Storage",
                                FallbackDirectory, ConfigFileName);
        }
    }
}
