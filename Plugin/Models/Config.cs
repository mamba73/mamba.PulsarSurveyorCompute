// Plugin/Models/Config.cs
using System;
using System.Xml.Serialization;
using VRageMath;

namespace Plugin.Models
{
    /// <summary>
    /// Persistent configuration for the Pulsar plugin.
    /// Saved/loaded as config.xml in LocalStorage.
    /// RULE: Nothing is hardcoded in service logic — all tunable values live here.
    /// </summary>
    public class Config
    {
        // --- HUD ---
        /// <summary>Screen-space anchor for the HUD overlay (0,0=top-left, 1,1=bottom-right).</summary>
        public Vector2 HudPosition { get; set; } = new Vector2(0.85f, 0.10f);

        // --- TUNNEL ---
        /// <summary>MyTransparentGeometry material for tunnel ring lines (e.g. "Square", "Dot").</summary>
        public string TunnelMaterial { get; set; } = "Square";
        /// <summary>Line thickness of tunnel ring edges in world-space units.</summary>
        public float TunnelLineThickness { get; set; } = 0.2f;
        /// <summary>Tunnel ring alpha transparency (0=invisible, 1=fully opaque).</summary>
        public float TunnelTransparency { get; set; } = 0.4f;
        /// <summary>Half-size of each tunnel ring frame in world-space meters.</summary>
        public float TunnelScale { get; set; } = 15f;

        // --- INPUT ---
        /// <summary>Keyboard hotkey used for the laser rangefinder ping (default: T).</summary>
        public string RangefinderHotkey { get; set; } = "T";

        // --- SURVEY / SCAN ---
        /// <summary>
        /// Sector cube size (meters) for GPS grouping logic.
        /// Ore detections within this radius of each other are candidates for merging.
        /// Default: 200m — one cluster per 200m cube.
        /// </summary>
        public float SectorSize { get; set; } = 200f;

        /// <summary>
        /// Maximum scan range (meters) applied to Ore Detector blocks.
        /// Controls both the auto-scan sphere and the Terminal slider upper limit.
        /// Default: 2500m.
        /// </summary>
        public float MaxDetectorRange { get; set; } = 2500f;

        /// <summary>Maximum range for the laser rangefinder (meters). Default: 50000m (50km).</summary>
        public double LaserMaxRange { get; set; } = 50000.0;

        /// <summary>
        /// Penetration depths (meters) sampled when reading ore material from a voxel surface.
        /// Multiple depths are tried in order; the first non-Stone result wins.
        /// Increase the deeper values if your asteroids have thick Stone shells.
        /// </summary>
        public float[] VoxelPenetrationDepths { get; set; } = { 0.5f, 1f, 2f, 5f, 10f, 20f };

        // --- PHYSICS ---
        /// <summary>
        /// Fallback thrust force (Newtons) used ONLY when no working thruster blocks are found.
        /// Under normal operation the plugin sums live MaxEffectiveThrust from all thrusters.
        /// </summary>
        public float DefaultThrustForce { get; set; } = 1000000f;

        /// <summary>
        /// Multiplier to determine the planetary gravity-influence zone radius.
        /// Zone radius = planet.AverageRadius * PlanetDetectionMultiplier.
        /// Default: 3.0 — captures typical SE gravity well extents.
        /// </summary>
        public double PlanetDetectionMultiplier { get; set; } = 3.0;

        /// <summary>Minimum speed (m/s) before the braking tunnel and collision checks are active.</summary>
        public float MinSpeedForTunnel { get; set; } = 1.0f;
    }
}
