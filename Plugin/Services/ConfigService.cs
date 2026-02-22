// Plugin/Services/ConfigService.cs
using System;
using System.Xml.Serialization;
using Plugin.Models;
using Sandbox.ModAPI;
using VRage.Utils;

namespace Plugin.Services
{
    public class ConfigService
    {
        private const string ConfigFileName = "config.xml";

        /// <summary>
        /// The active configuration. Always valid after Load() — guaranteed non-null.
        /// Falls back to defaults if the file is missing or XML parse fails,
        /// so services never need to null-check this property.
        /// </summary>
        public Config Data { get; private set; }

        /// <summary>
        /// Reads config.xml from LocalStorage.
        /// If the file doesn't exist, creates a default config and writes it to disk.
        /// On parse failure, logs the error and continues with default values.
        /// </summary>
        public void Load()
        {
            try
            {
                if (MyAPIGateway.Utilities.FileExistsInLocalStorage(ConfigFileName, typeof(Config)))
                {
                    using (var reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(ConfigFileName, typeof(Config)))
                    {
                        var serializer = new XmlSerializer(typeof(Config));
                        Data = (Config)serializer.Deserialize(reader);
                    }
                }
                else
                {
                    Data = new Config(); // First run — generate defaults
                    Save();
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[Pulsar] Config Load Error: {ex.Message}");
                Data = new Config(); // Continue with defaults rather than crashing
            }
        }

        /// <summary>
        /// Serializes current Config.Data to LocalStorage as config.xml.
        /// Called on plugin Dispose() and when settings are changed at runtime.
        /// </summary>
        public void Save()
        {
            try
            {
                using (var writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(ConfigFileName, typeof(Config)))
                {
                    var serializer = new XmlSerializer(typeof(Config));
                    serializer.Serialize(writer, Data);
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[Pulsar] Config Save Error: {ex.Message}");
            }
        }
    }
}
