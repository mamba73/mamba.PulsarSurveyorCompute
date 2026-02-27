# mamba.PulsarSurveyorCompute

---
**NOTE: Project Milestone Reached.** Pulsar Surveyor v1.0 is now a fully integrated flight and resource management system.

An advanced client-side plugin for Space Engineers providing real-time trajectory analysis, predictive braking assistance, and smart planetary/asteroid surveying.

Unlike standard scripts, Pulsar is a standalone plugin that leverages deep game engine hooks to calculate ship physics, predict collision paths via 3D rendering, and provides a spatial-hashing system for composite ore detection.

**C#**: 4.7.2+ / .NET Framework 4.8  
**Space Engineers**: 1.204+  
**Author**: mamba  
**Version**: 1.0.133

---
## 🚀 Project Status: Feature Complete (v1.0)

| Feature | Status | Notes |
| :--- | :---: | :--- |
| **Modular Architecture** | ✅ Done | Service-oriented injection (Physics, Telemetry, Input). |
| **Trajectory Tunnel** | ✅ Done | Predictive 3D visualization of braking distance. |
| **Smart GPS Surveying** | ✅ Done | Sector-based grouping of ores (e.g., "Iron, Gold"). |
| **Anti-Spam Logic** | ✅ Done | Spatial hashing (200m) to prevent GPS marker flood. |
| **Laser Rangefinder** | ✅ Done | Hotkey-based raycast distance measurement. |
| **Planet Telemetry** | ✅ Done | Real-time "True Altitude", Gravity (G), and fauna detection. |
| **HUD Overlay** | ✅ Done | Live telemetry: Mass, Deceleration, Altitude, Gravity, Range. |
| **Manual Reset** | ✅ Done | `Shift + T` clears all survey GPS markers and resets scan memory. |
| **Collision Warning** | ✅ Done | Audio-visual proximity alerts for impact. |
| **Planet Exclusion** | ✅ Done | Voxel scanner skips planetary bodies — asteroids only. |

---
## 🌟 Key Features

---
### 🟦 Predictive Trajectory Tunnel
A dynamic 3D visual guide that projects your stopping path.
- **Physics Driven:** Tunnel length is calculated using `(Velocity² / (2 * MaxDeceleration))`.
- **Color Coded:** Green = clear, Orange = caution zone, Red = BRAKE NOW.
- **Dynamic Feedback:** Reacts instantly to changes in ship mass or thruster damage.
- **Configurable:** Adjust transparency and scale via `config.xml`.

---
### 📡 Smart Voxel Surveyor (Asteroids)
The flagship "Surveyor" feature that identifies materials within asteroids.
- **Composite Labeling:** One GPS point per 200m sector, ore names appended as found. Example: `[Pulsar] S01 A01 (Iron, Gold, Uranium)`.
- **Spatial Hashing:** 200m cubic sectors keep the HUD clean regardless of flight speed.
- **Voxel Filtering:** Skips Stone and planetary voxel maps for performance and clean results.
- **Auto Scan:** Runs every 2 seconds via the active Ore Detector (no pilot input required).

---
### 📊 Flight Computer HUD
A high-performance overlay for critical pilot information:
- **Mass Tracking:** Real-time grid mass monitor.
- **Decel Capacity:** Maximum available m/s² to judge braking safety.
- **Gravity Readout:** Live G-force from the nearest planet.
- **Laser Range:** Precision distance tracking via the built-in rangefinder.
- **HUD Sync:** Respects the vanilla HUD visibility toggle (H key) — no flicker.

---
### 🌍 Planetary Telemetry
Deep detection of the nearest celestial body:
1. **True Altitude:** Distance to actual terrain surface, not sea level.
2. **Natural Gravity:** Live G-force readout.
3. **Fauna Detection:** Reads planet generator data to detect Spiders, Wolves, etc.
4. **Low-Altitude Warning:** Red HUD alert when descending fast below 2000m.

---
## ⌨️ Controls & Configuration

| Input | Action |
| :--- | :--- |
| `T` | Laser Rangefinder ping |
| `Shift + T` | Clear all Pulsar GPS markers / reset scan session |
| Terminal Panel | Ore Detector → Pulsar: Manual Scan / Range / Sector |

**config.xml** (in LocalStorage) — editable fields:

| Key | Default | Description |
| :--- | :--- | :--- |
| `SurveyRadius` | 500 | Sector cube size for spatial hashing (meters) |
| `DefaultThrustForce` | 1000000 | Fallback thrust in Newtons for physics calculations |
| `TunnelTransparency` | 0.4 | Tunnel alpha (0=invisible, 1=solid) |
| `TunnelScale` | 15 | Ring half-size in world meters |
| `TunnelMaterial` | Square | MyTransparentGeometry material name |
| `TunnelLineThickness` | 0.2 | Ring edge thickness |
| `PlanetDetectionMultiplier` | 3.0 | Gravity zone = planet.AverageRadius * this |

---
## 🗺️ Roadmap
1. **Ore Priority Coloring:** Highlight rare ores (Platinum/Uranium) differently in GPS descriptions.
2. **Planet Surface Deposits:** Optimized scanning for planetary ore patches.
3. **Thrust Measurement:** Replace DefaultThrustForce fallback with live thruster block summation.

---
## 🤝 Contributing
Maintain compatibility with .NET Framework 4.8.  
All code and comments must be in **English**.  
Pull requests should follow the existing service-oriented architecture.

---
## ☕ Support
If you like this project and want to support development:  
[Buy Me a Coffee ☕](https://buymeacoffee.com/mamba73)

*Developed by [mamba73](https://github.com/mamba73).*
