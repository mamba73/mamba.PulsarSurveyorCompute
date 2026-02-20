// Plugin/Models/Config.cs
using System;
using System.Xml.Serialization;
using VRageMath;

namespace Plugin.Models
{
    public class Config
    {
        public Vector2 HudPosition { get; set; } = new Vector2(0.85f, 0.10f);
        public string TunnelMaterial { get; set; } = "Square";
        public float TunnelLineThickness { get; set; } = 0.2f;
        public float TunnelTransparency { get; set; } = 0.4f;
        public string RangefinderHotkey { get; set; } = "T";
        public float SurveyRadius { get; set; } = 500f;
        public float DefaultThrustForce { get; set; } = 1000000f;
        public double PlanetDetectionMultiplier { get; set; } = 3.0;
        public float TunnelScale { get; set; } = 15f;
    }
}