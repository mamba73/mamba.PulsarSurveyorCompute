// Plugin/Models/Config.cs
using System;
using System.Xml.Serialization;
using VRageMath;

namespace Plugin.Models
{
    /// <summary>
    /// Persistent configuration for the Pulsar plugin.
    /// RULE: Every tunable value lives here — nothing hardcoded in service logic.
    /// </summary>
    public class Config
    {
        // --- HUD ---
        public Vector2 HudPosition { get; set; } = new Vector2(0.85f, 0.10f);

        // --- TUNNEL ---
        public string TunnelMaterial      { get; set; } = "Square";
        public float  TunnelLineThickness { get; set; } = 0.2f;
        public float  TunnelTransparency  { get; set; } = 0.4f;
        public float  TunnelScale         { get; set; } = 15f;

        // --- INPUT ---
        public string RangefinderHotkey { get; set; } = "T";

        // --- SURVEY / SCAN ---
        /// <summary>
        /// Pulsar's own scan radius (meters), completely independent of the vanilla
        /// Ore Detector block range. The vanilla block has a hard cap (~150m by definition);
        /// Pulsar uses entity-based scanning so this limit can be freely set up to 2500m.
        /// Controlled via the "Pulsar: Scan Range" slider in the Ore Detector terminal.
        /// </summary>
        public float PulsarScanRange { get; set; } = 1000f;

        /// <summary>
        /// Max value the "Pulsar: Scan Range" terminal slider will allow.
        /// Increase this if you want to scan further, but expect longer scan times.
        /// </summary>
        public float MaxScanRange { get; set; } = 2500f;

        /// <summary>
        /// Stride (meters) between sample points when scanning inside a voxel's storage.
        /// Smaller = more accurate ore detection but slower.
        /// Default 8m: catches ore veins ≥ 8m wide (most SE ores are 10-100m wide).
        /// </summary>
        public int VoxelScanStride { get; set; } = 8;

        /// <summary>
        /// GPS sector radius for grouping detections on the same physical asteroid.
        /// All ore found on one voxel entity goes to one GPS marker regardless of this.
        /// This value is used only for the spatial-hash fallback (non-entity detections).
        /// </summary>
        public float SectorSize { get; set; } = 500f;

        /// <summary>Max range for the laser rangefinder (meters). Default: 50 000m.</summary>
        public double LaserMaxRange { get; set; } = 50000.0;

        /// <summary>
        /// Penetration depths (meters) tried by the laser rangefinder when sampling
        /// voxel material. First non-Stone result wins.
        /// </summary>
        public float[] VoxelPenetrationDepths { get; set; } = { 0.5f, 1f, 2f, 5f, 10f, 20f };

        // --- PHYSICS ---
        /// <summary>Fallback thrust (N) when no working thrusters are found.</summary>
        public float DefaultThrustForce { get; set; } = 1000000f;

        /// <summary>Planet gravity zone = AverageRadius × this multiplier.</summary>
        public double PlanetDetectionMultiplier { get; set; } = 3.0;

        /// <summary>Minimum speed (m/s) to activate the braking tunnel.</summary>
        public float MinSpeedForTunnel { get; set; } = 1.0f;

        /// <summary>
        /// Distance from gravity well edge (meters) at which the "gravity approach" 
        /// warning starts showing on the HUD. Default: 5000m before the well boundary.
        /// </summary>
        public float GravityWellWarnDistance { get; set; } = 5000f;
    }
}
