using System;
using System.Xml.Serialization;
using VRageMath;

namespace Plugin.Models
{
    public class Config
    {
        public Vector2 HudPosition { get; set; } = new Vector2(0.85f, 0.10f);
        public float TunnelTransparency { get; set; } = 0.4f;
        public string RangefinderHotkey { get; set; } = "T";
        public float SurveyRadius { get; set; } = 500f;
    }
}