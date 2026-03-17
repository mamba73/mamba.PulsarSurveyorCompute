// Plugin/Services/ChatCommandService.cs
using System;
using System.Text;
using Sandbox.ModAPI;
using VRage.Game;

namespace Plugin.Services
{
    public class ChatCommandService
    {
        private readonly GpsManagerService _gpsManager;

        // MAMBA EXPLANATION: Flag to ensure we don't register the event multiple times.
        private bool _isRegistered = false;

        public ChatCommandService(GpsManagerService gpsManager)
        {
            _gpsManager = gpsManager;
        }

        // MAMBA EXPLANATION: Registers the chat hook. This should be called from MainPlugin.Update()
        // ensuring it only runs after MyAPIGateway is fully initialized, preventing Pulsar loader crashes.
        public void Register()
        {
            if (_isRegistered) return;

            if (MyAPIGateway.Utilities != null)
            {
                MyAPIGateway.Utilities.MessageEntered += OnMessageEntered;
                _isRegistered = true;
            }
        }

        // MAMBA EXPLANATION: Unregisters the chat hook to prevent memory leaks when the session ends.
        public void Unregister()
        {
            if (!_isRegistered || MyAPIGateway.Utilities == null) return;

            MyAPIGateway.Utilities.MessageEntered -= OnMessageEntered;
            _isRegistered = false;
        }

        // MAMBA EXPLANATION: The event handler for intercepting chat messages.
        private void OnMessageEntered(string messageText, ref bool sendToOthers)
        {
            if (string.IsNullOrWhiteSpace(messageText) || !messageText.StartsWith("/psc", StringComparison.OrdinalIgnoreCase))
                return;

            // MAMBA EXPLANATION: Stop the command from being broadcasted to the server/other players.
            sendToOthers = false;
            string[] parts = messageText.Split(' ');

            if (parts.Length == 1 || parts[1].Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                ShowHelp();
                return;
            }

            if (parts[1].StartsWith("scan:ore", StringComparison.OrdinalIgnoreCase))
            {
                float radius = _gpsManager.PulsarScanRange;
                if (parts.Length > 2)
                {
                    float.TryParse(parts[2], out radius);
                }

                _gpsManager.PulsarScanRange = radius;

                // MAMBA EXPLANATION: Call the existing sector scan logic. Passing null because 
                // chat commands do not have a specific terminal block source.
                _gpsManager.ForceSectorScan(null);
                return;
            }
        }

        // MAMBA EXPLANATION: Displays the help screen using the native mission screen UI.
        private void ShowHelp()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Pulsar Client Commands:");
            sb.AppendLine("  /psc help - Shows this list");
            sb.AppendLine("  /psc scan:ore [radius] - Scans asteroids in range (default uses configured radius)");
            MyAPIGateway.Utilities.ShowMissionScreen("PSC Help", "", "", sb.ToString(), null, "Close");
        }
    }
}