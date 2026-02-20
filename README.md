# mamba.PulsarSurveyorCompute

---
**NOTE: This project is in active development.** High-fidelity flight computations for Space Engineers.

An advanced client-side plugin for Space Engineers providing real-time trajectory analysis, predictive braking assistance, and planetary telemetry.

Unlike standard scripts, Pulsar is a standalone plugin that leverages deep game engine hooks to calculate ship physics, predict collision paths via 3D rendering, and provide a configurable HUD for deep-space surveying.

**C#**: 4.7.2+ / .NET Framework 4.8  
**Space Engineers**: 1.204+  
**Author**: mamba  
**Version**: 1.0.45

---
## 🚀 Project Status: Stable Core

| Feature | Status | Notes |
| :--- | :---: | :--- |
| **Modular Architecture** | ✅ Done | Service-oriented injection (Physics, Telemetry, Input). |
| **Config Handling (XML)** | ✅ Done | Zero hardcoded values. Everything via config.xml. |
| **Trajectory Tunnel** | ✅ Done | Predictive 3D visualization of braking distance. |
| **Physics Service** | ✅ Done | Accurate Mass/Deceleration calculations. |
| **Planet Telemetry** | ✅ Done | Real-time altitude and gravity detection. |
| **Laser Rangefinder** | ✅ Done | Hotkey-based raycast distance measurement. |
| **HUD Overlay** | ✅ Done | Configurable screen position for critical flight data. |
| **Ore Surveyor** | 🚧 In Progress | Auto-mapping asteroid resources within radius. |
| **Dynamic Collision Warn** | ⏳ Planned | Audio-visual proximity alerts based on velocity. |

---
## 🌟 Key Features

---
### 🟦 Predictive Trajectory Tunnel
A dynamic 3D visual guide that appears when the ship is in motion.
- **Physics Based:** The tunnel length adjusts in real-time based on your current velocity and the maximum deceleration capacity of your thrusters.
- **Safety Indicators:** Changes color (Green/Orange/Red) based on collision detection with asteroids or planet surfaces.
- **Configurable:** Adjust transparency, scale, and material via configuration.

---
### 📊 Real-Time Flight Computer (HUD)
A clean, non-intrusive HUD overlay displaying live ship data.
- **Dynamic Mass Tracking:** Monitors real-time grid mass changes.
- **Max Deceleration:** Know exactly how many m/s² your ship can handle.
- **Altimeter:** Displays distance to actual ground level, not just center-of-planet.

---
### 🔭 Laser Rangefinder
- **Precision Targeting:** Measure distance to any grid or voxel map instantly.
- **Hotkey Driven:** Default [T] triggers a high-performance raycast up to 50km.

---
### 🌍 Planetary Telemetry
The system dynamically detects the nearest celestial body:
1. **Detection Multiplier:** Scans for planets within a configurable range of their radius.
2. **Surface Analysis:** Calculates the closest surface point to provide "True Altitude."
3. **Gravity Monitoring:** Displays natural gravity magnitude in G-force.

---
## 🗺️ Roadmap

### 1. Ore Mapping (Surveyor)
Expanding the GpsManagerService to automatically create temporary GPS markers for ores when an Ore Detector is active on the ship.

### 2. SQLite Integration
Moving configuration and discovery data to SQLite for high-performance logging and session persistence.

### 3. Flight Path Optimization
Calculating the most fuel-efficient burn times for planetary exit and entry.

---
## 🤝 Contributing
Maintain compatibility with .NET Framework 4.8.
Ensure all logic remains decoupled from the Main Plugin entry point.

---
## ☕ Support
If you like this project and want to support development:
[Buy Me a Coffee ☕](https://buymeacoffee.com/mamba73)

*Developed by [mamba73](https://github.com/mamba73).*
