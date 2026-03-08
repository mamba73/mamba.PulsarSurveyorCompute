// Plugin/Services/ConfigService.cs
using System;
using System.IO;
using System.Xml.Serialization;
using Plugin.Models;
using VRage.Utils;

namespace Plugin.Services
{
    /// <summary>
    /// Manages persistent config.xml at a fixed filesystem path:
    ///   %AppData%\SpaceEngineers\Storage\PulsarSurveyorCompute\config.xml
    ///
    /// WHY NOT MyAPIGateway.Utilities.LocalStorage:
    ///   LocalStorage uses typeof(T) for directory naming. Pulsar/Roslyn recompiles
    ///   the plugin on every launch with a random assembly token, which means
    ///   typeof(Config) resolves to a different identity each run →
    ///   a new random directory is created every time (e.g. mamba.PSC_4wpwe02b.ksw\).
    ///   A fixed filesystem path avoids this entirely and is world-independent,
    ///   which is appropriate for a plugin (not a mod — no per-save config needed).
    ///
    /// LOAD SEQUENCE:
    ///   Init()    → defaults only (no file I/O, session not ready)
    ///   Update()  → TryLoadOnce() on first tick → reads config.xml
    ///   Dispose() → Save() writes any runtime changes back
    /// </summary>
    public class ConfigService
    {
        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SpaceEngineers", "Storage", "PulsarSurveyorCompute", "config.xml");

        private bool _loaded = false;

        /// <summary>Active configuration. Non-null from construction.</summary>
        public Config Data { get; private set; } = new Config();

        // -----------------------------------------------------------------------
        // PRIMARY INTERFACE
        // -----------------------------------------------------------------------

        /// <summary>
        /// Called from MainPlugin.Update() each tick until loading succeeds.
        /// No-op once loaded.
        /// </summary>
        public void TryLoadOnce()
        {
            if (_loaded) return;
            Load();
        }

        public void Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    using (var reader = new StreamReader(ConfigPath))
                    {
                        var ser = new XmlSerializer(typeof(Config));
                        Data = (Config)ser.Deserialize(reader);
                        MyLog.Default.WriteLineAndConsole($"[PSC] Config loaded from {ConfigPath}");
                    }
                }
                else
                {
                    Data = new Config();
                    Save(); // write defaults on first run
                    MyLog.Default.WriteLineAndConsole($"[PSC] Config created with defaults at {ConfigPath}");
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PSC] Config load error: {ex.Message} — using defaults.");
                Data = new Config();
            }
            finally
            {
                _loaded = true;
            }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
                using (var writer = new StreamWriter(ConfigPath))
                {
                    var ser = new XmlSerializer(typeof(Config));
                    ser.Serialize(writer, Data);
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[PSC] Config save error: {ex.Message}");
            }
        }
    }
}
