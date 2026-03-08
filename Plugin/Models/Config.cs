// Plugin/Models/Config.cs
using System.Xml.Serialization;
using VRageMath;

namespace Plugin.Models
{
    /// <summary>
    /// Persistent configuration. RULE: All tunable values live here — nothing hardcoded in service logic.
    /// </summary>
    public class Config
    {
        // ===================================================================
        // VERSION (read-only display — do not edit in config.xml)
        // ===================================================================
        /// <summary>Plugin version shown in terminal label and startup notification.
        /// This is set by the build system and should not be manually edited.</summary>
        public string PluginVersion { get; set; } = "1.0.138";

        // ===================================================================
        // TUNNEL
        // ===================================================================
        /// <summary>MyTransparentGeometry material name for tunnel ring lines.
        /// Valid built-in SE values: "Square" (solid line), "WhiteBlock", "Dot", "ContainerBorder".
        /// "Circle" is NOT a valid SE geometry material and will cause invisible rings.
        /// To try alternatives: change and rebuild. "Square" is most reliable.</summary>
        public string TunnelMaterial      { get; set; } = "Square";
        public float  TunnelLineThickness { get; set; } = 0.15f;
        /// <summary>Base alpha for nearest ring. Far rings fade to ~0 via quadratic falloff.
        /// 0.08–0.15 = subtle/virtual. 0.3+ = very visible.</summary>
        public float  TunnelTransparency  { get; set; } = 0.10f;
        /// <summary>Half-size (m) of each tunnel ring cross-section.</summary>
        /// <summary>DEPRECATED — tunnel now scales to actual ship bounding box.
        /// Kept for XML compatibility, no longer used by renderer.</summary>
        [System.Xml.Serialization.XmlIgnore]
        public float  TunnelScale         { get; set; } = 15f;
        /// <summary>Distance (m) between tunnel rings. Rule: ~cruise_speed / 5.
        /// At 100 m/s → 20m. At 500 m/s → 100m. Default 80m works for most speeds.</summary>
        public float  TunnelRingSpacing   { get; set; } = 100f;
        /// <summary>Minimum speed (m/s) to render the braking tunnel.</summary>
        public float  MinSpeedForTunnel   { get; set; } = 5.0f;

        /// <summary>
        /// Fraction of stopping distance at which tunnel turns ORANGE (caution).
        /// 1.0 = full stopping distance (very early warning).
        /// Example: speed=100m/s, decel=5m/s² → stopping=1000m.
        ///   OrangeThreshold=2.0 → orange when obstacle within 2000m (2× stopping dist).
        ///   OrangeThreshold=1.0 → orange when obstacle within 1000m (exactly stopping dist).
        /// Default 1.5 = warn at 1.5× stopping distance — gives plenty of reaction time.
        /// Set higher (2.0–3.0) for early warning, lower (0.8) for late warning.
        /// </summary>
        public float  TunnelOrangeThreshold { get; set; } = 1.5f;

        /// <summary>
        /// Fraction of stopping distance at which tunnel turns RED (brake NOW).
        /// Must be less than OrangeThreshold.
        /// Default 0.6 = red when obstacle within 60% of stopping distance.
        /// At that point you need to brake immediately or you will collide.
        /// Set lower (0.3) if you trust your reaction time, higher (0.8) for safety.
        /// </summary>
        public float  TunnelRedThreshold    { get; set; } = 0.6f;

        // ===================================================================
        // HUD — WHAT TO SHOW
        // ===================================================================
        /// <summary>Show ship mass on HUD. Disable if you already read it from SE native HUD.</summary>
        public bool HudShowMass    { get; set; } = false;
        /// <summary>Show max deceleration (m/s²) on HUD.</summary>
        public bool HudShowDecel   { get; set; } = true;
        /// <summary>Show terrain altitude on HUD.</summary>
        public bool HudShowAlt     { get; set; } = true;
        /// <summary>Show natural gravity in G on HUD.</summary>
        public bool HudShowGravity { get; set; } = true;
        /// <summary>Show last laser rangefinder distance on HUD.</summary>
        public bool HudShowLaser   { get; set; } = true;
        /// <summary>Show planet approach block (name, GW distance, gravity sustainability) on HUD.</summary>
        public bool HudShowPlanet  { get; set; } = true;

        // ===================================================================
        // HUD — REFRESH RATE
        // ===================================================================
        /// <summary>
        /// How often (in game ticks) the full planet search runs.
        /// 60 ticks = 1 second. Default 1800 = 30 seconds.
        /// Increase if you travel between planets frequently (try 300 = 5s).
        /// Decrease if you notice stale planet info (will slightly increase CPU usage).
        /// Planet data is cached between refreshes — fast jumps may show stale data
        /// until the next refresh cycle completes.
        /// </summary>
        /// <summary>Enable periodic planet data refresh. When false, planet info is
        /// computed once on cockpit entry and never updated. Enable if you jump between planets.</summary>
        public bool PlanetRefreshEnabled { get; set; } = true;

        /// <summary>How often (ticks) the planet selection reruns. 60 ticks = 1 second.
        /// Default 1800 = 30s. Reduce to 300 (5s) if you jump frequently.
        /// Has no effect when PlanetRefreshEnabled = false.</summary>
        public int PlanetRefreshTicks { get; set; } = 1800;

        // ===================================================================
        // GRAVITY WELL VISUALIZATION
        // ===================================================================
        /// <summary>Show gravity well sphere while in cockpit (first-person view).</summary>
        public bool GravityWellShowCockpit  { get; set; } = true;
        /// <summary>Show gravity well sphere while in external/third-person view.
        /// Disable if external view causes lag (sphere draws many line segments).</summary>
        public bool GravityWellShowExternal { get; set; } = false;

        /// <summary>Color of the gravity well visualization rings (RGBA, 0–255 each).</summary>
        public SerializableVector4 GravityWellColor { get; set; }
            = new SerializableVector4 { X = 0.4f, Y = 0.7f, Z = 1.0f, W = 0.18f };

        /// <summary>
        /// Maximum distance from planet CENTER (meters) within which the gravity well
        /// sphere becomes visible. Set to 0 to always show when inside the well.
        /// Default: 0 (show whenever inside gravity well radius).
        /// Example: set to 500000 to start showing when within 500km of center.
        /// </summary>
        public double GravityWellShowRadius { get; set; } = 0;

        /// <summary>Number of points used to draw each circle of the gravity well sphere.
        /// More = smoother circle but more draw calls. Default 64.</summary>
        public int GravityWellSegments { get; set; } = 64;

        // ===================================================================
        // SURVEY / SCAN
        // ===================================================================
        /// <summary>Default scan radius (m) for the sector ore scan.
        /// Also the initial slider value shown in the Ore Detector terminal.</summary>
        public float  PulsarScanRange  { get; set; } = 2500f;

        /// <summary>Extra clearance (meters) added to ship half-extents for collision rays.
        /// Accounts for maneuvering space. Default 4m.</summary>
        public float  CollisionMargin  { get; set; } = 4f;

        /// <summary>Maximum value of the scan range slider in the terminal UI.</summary>
        public float  MaxScanRange     { get; set; } = 25000f;

        /// <summary>Stride (in LOD2 cells) for the voxel scan. 1 = thorough, 2 = faster.
        /// At LOD2 one cell = 4m³. Stride 1 checks every 4m, stride 2 every 8m.
        /// Ore veins can be as narrow as 3-5m so stride 1 is recommended.</summary>
        public int    VoxelScanStride  { get; set; } = 1;

        /// <summary>Sector name prefix used in GPS labels (e.g. "S01 A01 Iron").</summary>
        public float  SectorSize       { get; set; } = 1000f;

        /// <summary>Maximum range (m) for the laser rangefinder (T key).</summary>
        public double LaserMaxRange    { get; set; } = 50000.0;

        /// <summary>
        /// Penetration depths (meters) for laser ore sampling.
        ///
        /// When the laser hits an asteroid surface, Pulsar samples the voxel material
        /// at each of these depths below the impact point. This is needed because the
        /// surface layer is almost always Stone — ore veins start a few meters underneath.
        ///
        /// How it works:
        ///   depth 0.5m → surface check (usually Stone)
        ///   depth 1-2m → shallow subsurface (thin Stone shell)
        ///   depth 5-10m → typical ore vein depth for most SE asteroid types
        ///   depth 20m  → catches deep veins in large asteroids
        ///
        /// You can extend this to 40f, 60f, 100f etc. for very large asteroids.
        /// Each additional depth adds one voxel read per laser shot — negligible cost.
        /// The scan stops at the first non-Stone ore found and returns that material.
        ///
        /// If all depths return Stone, the asteroid has no detectable ore along that ray.
        /// Try shooting a different surface point — ore veins are not evenly distributed.
        /// </summary>
        /// <summary>
        /// Keyboard key for the full asteroid deep-scan (all ores, async).
        /// Uses VRage.Input.MyKeys enum name as a string.
        /// Default "Y" (next to T which is the laser ping key).
        /// Other suggestions: "U", "H", "Numpad0".
        /// Warning: avoid keys already used by SE (G=jetpack, R=reload, F=use, etc.)
        /// </summary>
        public string FullScanKey { get; set; } = "Y";

        public float[] VoxelPenetrationDepths { get; set; } = { 0.5f, 1f, 2f, 5f, 10f, 20f, 40f };

        // ===================================================================
        // PHYSICS
        // ===================================================================
        public float  DefaultThrustForce         { get; set; } = 1000000f;
        public double PlanetDetectionMultiplier  { get; set; } = 3.0;
        public float  GravityWellWarnDistance    { get; set; } = 5000f;
    }

    /// <summary>
    /// XML-serializable Vector4 (VRageMath.Vector4 is not XML-serializable by default).
    /// </summary>
    public class SerializableVector4
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float W { get; set; }

        public Vector4 ToVector4() => new Vector4(X, Y, Z, W);
        public Color   ToColor()   => new Color(X, Y, Z, W);
    }
}
