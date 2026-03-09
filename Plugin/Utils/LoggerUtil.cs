// Plugin/Utils/LoggerUtil.cs
//
// Writes a timestamped log file to:
//   %AppData%\SpaceEngineers\Storage\mamba.PulsarSurveyorCompute\log\
//   Filename: YYYY-MM-DD_HHMMSS_mamba.PulsarSurveyorCompute_v{version}.log
//
// One file per game session (created on Init, written until Dispose).
// All [PSC] entries also go to SpaceEngineers.Log via MyLog.Default.
//
using System;
using System.IO;
using Plugin.Models;
using VRage.Utils;

namespace Plugin.Utils
{
    public static class LoggerUtil
    {
        private static readonly object _lock = new object();
        private static string _logFile;
        private static bool   _ready = false;

        public static readonly string StorageRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SpaceEngineers", "Storage", "mamba.PulsarSurveyorCompute");

        public static string LogDir => Path.Combine(StorageRoot, "log");

        // -----------------------------------------------------------------------
        // INIT / DISPOSE
        // -----------------------------------------------------------------------

        /// <summary>
        /// Call once from MainPlugin.Init(). Creates the log file.
        /// Version is read from Config defaults (before file load — that's fine,
        /// version is always baked in at compile time).
        /// </summary>
        public static void Initialize(string version)
        {
            try
            {
                Directory.CreateDirectory(LogDir);

                string date    = DateTime.Now.ToString("yyyy-MM-dd");
                string time    = DateTime.Now.ToString("HHmmss");
                string fname   = $"{date}_{time}_mamba.PulsarSurveyorCompute_v{version}.log";
                _logFile = Path.Combine(LogDir, fname);

                // Write header
                var header =
                    $"# date: {date}\n" +
                    $"# time: {time}\n" +
                    $"# project: mamba.PulsarSurveyorCompute\n" +
                    $"# version: {version}\n" +
                    $"# log: {fname}\n" +
                    new string('-', 72) + "\n";

                lock (_lock)
                    File.WriteAllText(_logFile, header);

                _ready = true;
                Info($"Logger initialized. Log: {_logFile}");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole(
                    $"[PSC] LoggerUtil init failed: {ex.Message}");
            }
        }

        /// <summary>Write final entry and flush. Call from MainPlugin.Dispose().</summary>
        public static void Shutdown()
        {
            Info("Plugin disposed — session end.");
            _ready = false;
        }

        // -----------------------------------------------------------------------
        // LOG METHODS
        // -----------------------------------------------------------------------

        public static void Info(string message)    => Write("INFO ",  message);
        public static void Warn(string message)    => Write("WARN ",  message);
        public static void Error(string message)   => Write("ERROR", message);
        public static void Debug(string message)   => Write("DEBUG", message);
        public static void Success(string message) => Write("OK   ",  message);

        // -----------------------------------------------------------------------
        // INTERNAL
        // -----------------------------------------------------------------------

        private static void Write(string level, string message)
        {
            string ts   = DateTime.Now.ToString("HH:mm:ss.fff");
            string line = $"[{ts}] [{level}] {message}";

            // Always write to SE main log
            MyLog.Default.WriteLineAndConsole($"[PSC] {line}");

            if (!_ready || _logFile == null) return;

            try
            {
                lock (_lock)
                    File.AppendAllText(_logFile, line + Environment.NewLine);
            }
            catch { /* never crash the game over logging */ }
        }
    }
}
