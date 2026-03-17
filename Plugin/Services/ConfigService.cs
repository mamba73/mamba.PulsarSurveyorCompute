// Plugin/Services/ConfigService.cs
using System;
using System.IO;
using System.Xml.Serialization;
using Plugin.Config;
using Plugin.Models;
using Plugin.Utils;

namespace Plugin.Services
{
    /// <summary>
    /// Manages persistent config.xml at a fixed filesystem path:
    ///   %AppData%\SpaceEngineers\Storage\mamba.PulsarSurveyorCompute\config.xml
    ///
    /// WHY NOT MyAPIGateway.Utilities.LocalStorage:
    ///   LocalStorage uses typeof(T) for directory naming. Pulsar/Roslyn recompiles
    ///   the plugin on every launch with a random assembly token, which means
    ///   typeof(Config) resolves to a different identity each run →
    ///   a new random directory is created every time
    ///   (e.g. mamba.PulsarSurveyorCompute_4wpwe02b.ksw\).
    ///   A fixed filesystem path avoids this entirely.
    ///
    /// LOAD SEQUENCE:
    ///   Init()    → defaults only, LoggerUtil.Initialize() called
    ///   Update()  → TryLoadOnce() on first tick → reads config.xml
    ///   Dispose() → Save()
    /// </summary>
    public class ConfigService
    {
        public static readonly string ConfigPath = Path.Combine(
            LoggerUtil.StorageRoot, "config.xml");

        private bool _loaded = false;

        /// <summary>Active configuration. Non-null from construction.</summary>
        public PluginConfig Data { get; private set; } = new PluginConfig();

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
                        var ser = new XmlSerializer(typeof(PluginConfig));
                        Data = (PluginConfig)ser.Deserialize(reader);
                    }
                    LoggerUtil.Info($"Config loaded from {ConfigPath}");
                }
                else
                {
                    Data = new PluginConfig();
                    Save();
                    LoggerUtil.Info($"Config created with defaults at {ConfigPath}");
                }
            }
            catch (Exception ex)
            {
                LoggerUtil.Error($"Config load error: {ex.Message} — using defaults.");
                Data = new PluginConfig();
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
                    var ser = new XmlSerializer(typeof(PluginConfig));
                    ser.Serialize(writer, Data);
                }
                LoggerUtil.Info("Config saved.");
            }
            catch (Exception ex)
            {
                LoggerUtil.Error($"Config save error: {ex.Message}");
            }
        }
    }
}
