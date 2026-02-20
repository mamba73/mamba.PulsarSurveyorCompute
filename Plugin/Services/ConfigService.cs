// Plugin/Services/ConfigService.cs
using System;
using System.IO;
using System.Xml.Serialization;
using Plugin.Models;
using Sandbox.ModAPI;
using VRage.Utils;

namespace Plugin.Services
{
    public class ConfigService
    {
        private const string ConfigFileName = "config.xml";
        public Config Data { get; private set; }

        public void Load()
        {
            try
            {
                if (MyAPIGateway.Utilities.FileExistsInLocalStorage(ConfigFileName, typeof(Config)))
                {
                    using (var reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(ConfigFileName, typeof(Config)))
                    {
                        XmlSerializer serializer = new XmlSerializer(typeof(Config));
                        Data = (Config)serializer.Deserialize(reader);
                    }
                }
                else
                {
                    Data = new Config();
                    Save();
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"[Pulsar] Config Load Error: {ex.Message}");
                Data = new Config();
            }
        }

        public void Save()
        {
            try
            {
                using (var writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(ConfigFileName, typeof(Config)))
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(Config));
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