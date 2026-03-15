// Plugin/ConfigUI/PluginConfigBridge.cs
//
// PORTABLE MODULE — part of the ConfigUI/ portable set.
// Drop this + AutoConfigUI.cs into any Pulsar plugin project.
// Update the namespace below to match your project.
//
using Sandbox.Graphics.GUI;  // MyGuiSandbox lives here, not in Sandbox.Game.Gui

namespace PulsarTemplate.ConfigUI
{
    /*
     * PluginConfigBridge
     * ------------------
     * Exposes plugin configuration to the Space Engineers in-game
     * plugin settings screen.
     *
     * Wraps any config object — no interface on Config required.
     *
     * Register in MainPlugin:
     *   public IPluginConfig GetPluginConfig() =>
     *       new PluginConfigBridge(_config, "My Plugin", "Configure behaviour.");
     *
     * NOTE: IPluginConfig / IMyPluginConfig availability depends on the
     *       Pulsar.Shared version in use. Adjust the interface name if needed.
     */
    public class PluginConfigBridge
    {
        private readonly object _config;

        public string Name        { get; }
        public string Description { get; }

        public PluginConfigBridge(
            object config,
            string name        = "Pulsar Plugin Settings",
            string description = "Configure behaviour of the plugin.")
        {
            _config     = config;
            Name        = name;
            Description = description;
        }

        /// <summary>Called by the SE plugin manager to open the settings screen.</summary>
        public void Draw()
        {
            // MyGuiSandbox is in Sandbox.Graphics.GUI — not Sandbox.Game.Gui
            MyGuiSandbox.AddScreen(new AutoConfigUI(_config));
        }

        /// <summary>Persist settings here if needed (e.g. write to XML).</summary>
        public void Save() { }
    }
}
