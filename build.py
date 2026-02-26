# build.py
import os
import re
import shutil
import subprocess
import sys
import glob
from datetime import datetime

# ---------------------------------------------------------------------------
# AUTOMATIC CONFIGURATION
# Project name and all paths are derived from the .csproj file.
# Nothing is hardcoded here except MSBuild path — adjust for your VS edition.
# ---------------------------------------------------------------------------
csproj_files = glob.glob("*.csproj")
if not csproj_files:
    print("[!] ERROR: No .csproj file found in current directory!")
    sys.exit(1)

PROJ_FILE    = csproj_files[0]
PROJECT_NAME = os.path.splitext(PROJ_FILE)[0]

# Adjust year/edition if your Visual Studio differs (2019, 2022, BuildTools, Community, etc.)
# MSBUILD_PATH = r"C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
MSBUILD_PATH = r"C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\MSBuild.exe"

# Pulsar stores plugins at: %APPDATA%\Pulsar\Legacy\Local\<ProjectName>\
BASE_TARGET_DIR    = os.path.join(os.getenv('APPDATA'), r"Pulsar\Legacy\Local")
PROJECT_TARGET_DIR = os.path.join(BASE_TARGET_DIR, PROJECT_NAME)

XML_SOURCE   = f"{PROJECT_NAME}.xml"
VERSION_FILE = "version.txt"
README_FILE  = "README.md"
CONFIG_CS    = os.path.join("Plugin", "Models", "Config.cs")


def ensure_xml_exists():
    """Creates a default Pulsar plugin manifest XML if one does not already exist."""
    if not os.path.exists(XML_SOURCE):
        print(f"[INFO] Creating default XML manifest: {XML_SOURCE}")
        xml_content = f"""<?xml version="1.0" encoding="utf-8"?>
<PluginData xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:type="GitHubPlugin">
    <Id>{PROJECT_NAME}</Id>
    <FriendlyName>{PROJECT_NAME}</FriendlyName>
    <Author>mamba</Author>
    <Description>Advanced Space Engineers surveying and flight computer plugin.</Description>
</PluginData>"""
        with open(XML_SOURCE, "w", encoding="utf-8") as f:
            f.write(xml_content)


def get_version():
    """
    Reads version.txt, increments the patch segment, writes it back, and returns the new version.
    Example: 1.0.98 → 1.0.99
    """
    if not os.path.exists(VERSION_FILE):
        with open(VERSION_FILE, "w") as f:
            f.write("1.0.0")
        return "1.0.0"

    with open(VERSION_FILE, "r") as f:
        v = f.read().strip()

    try:
        parts = v.split(".")
        parts[-1] = str(int(parts[-1]) + 1)
        new_v = ".".join(parts)
    except Exception:
        new_v = "1.0.1"

    with open(VERSION_FILE, "w") as f:
        f.write(new_v)

    return new_v


def update_readme_version(v):
    """
    Finds the version line in README.md and replaces it with the new version.
    Matches the pattern: **Version**: X.X.X
    This is called automatically after every successful build so the README
    always reflects the current deployed version.
    """
    if not os.path.exists(README_FILE):
        print(f"[WARN] {README_FILE} not found — skipping version update.")
        return

    with open(README_FILE, "r", encoding="utf-8") as f:
        content = f.read()

    updated = re.sub(
        r"(\*\*Version\*\*:\s*)[\d.]+",
        lambda m: f"{m.group(1)}{v}",
        content
    )

    if updated == content:
        print(f"[WARN] Could not find '**Version**: X.X.X' pattern in {README_FILE}.")
    else:
        with open(README_FILE, "w", encoding="utf-8") as f:
            f.write(updated)
        print(f"[OK] README.md version updated to {v}.")


def update_config_version(v):
    """
    Updates the PluginVersion default value in Plugin/Models/Config.cs.

    Matches the line:
        public string PluginVersion { get; set; } = "X.X.X";

    Called BEFORE building so that the compiled binary and deployed source
    both contain the correct version string. Also called during rollback to
    keep Config.cs in sync with version.txt on build failure.
    """
    if not os.path.exists(CONFIG_CS):
        print(f"[WARN] {CONFIG_CS} not found — skipping Config.cs version update.")
        return

    with open(CONFIG_CS, "r", encoding="utf-8") as f:
        content = f.read()

    updated = re.sub(
        r'(public string PluginVersion \{ get; set; \} = ")[^"]+(")',
        lambda m: f'{m.group(1)}{v}{m.group(2)}',
        content
    )

    if updated == content:
        print(f"[WARN] PluginVersion pattern not found in {CONFIG_CS} — check field exists.")
    else:
        with open(CONFIG_CS, "w", encoding="utf-8") as f:
            f.write(updated)
        print(f"[OK] Config.cs PluginVersion → {v}")


def run_build(v):
    """
    Invokes MSBuild in Release/x64 mode with the new version number.
    Returns True on success, False if MSBuild is missing or compilation fails.
    """
    print(f"\n>>> BUILDING VERSION {v}")
    if not os.path.exists(MSBUILD_PATH):
        print(f"[!] MSBuild not found at: {MSBUILD_PATH}")
        print("[!] Edit MSBUILD_PATH in build.py to match your Visual Studio installation.")
        return False

    cmd = [
        MSBUILD_PATH, PROJ_FILE,
        "/t:Restore;Rebuild",
        "/p:Configuration=Release",
        "/p:Platform=x64",
        f"/p:Version={v}",
        "/v:minimal"
    ]
    return subprocess.run(cmd).returncode == 0


def deploy(v):
    """
    Copies all required artifacts to the Pulsar AppData plugin directory.
    Four items are deployed on each build:
      1. Compiled plugin DLL    — used if Pulsar runs in binary mode
      2. Plugin manifest XML    — required by the Pulsar loader (.dll.xml)
      3. Plugin C# source tree  — used by Pulsar's Roslyn on-the-fly compiler
      4. Pulsar.Shared.dll      — interface DLL needed at Roslyn compile time
    """
    print(f"\n>>> DEPLOYING v{v} → {PROJECT_TARGET_DIR}")

    dll_name      = f"{PROJECT_NAME}.dll"
    xml_dest_name = f"{PROJECT_NAME}.dll.xml"  # Pulsar naming convention

    src_dll        = os.path.join("bin", "Release", dll_name)
    src_pulsar_dll = os.path.join("Dependencies", "Pulsar.Shared.dll")

    os.makedirs(PROJECT_TARGET_DIR, exist_ok=True)

    # 1. Plugin DLL
    if os.path.exists(src_dll):
        shutil.copy(src_dll, os.path.join(PROJECT_TARGET_DIR, dll_name))
        print(f"[OK] DLL deployed: {dll_name}")
    else:
        print(f"[WARN] DLL not found at: {src_dll}")

    # 2. Plugin manifest XML
    if os.path.exists(XML_SOURCE):
        shutil.copy(XML_SOURCE, os.path.join(PROJECT_TARGET_DIR, xml_dest_name))
        print(f"[OK] XML deployed: {xml_dest_name}")

    # 3. C# source tree (fresh copy — removes stale .cs files from previous builds)
    target_plugin_dir = os.path.join(PROJECT_TARGET_DIR, "Plugin")
    if os.path.exists("Plugin"):
        if os.path.exists(target_plugin_dir):
            shutil.rmtree(target_plugin_dir)
        shutil.copytree("Plugin", target_plugin_dir)
        print(f"[OK] Source deployed: {target_plugin_dir}")

    # 4. Pulsar.Shared.dll (Roslyn needs this to resolve IPulsarPlugin)
    if os.path.exists(src_pulsar_dll):
        shutil.copy(src_pulsar_dll, os.path.join(PROJECT_TARGET_DIR, "Pulsar.Shared.dll"))
        print(f"[OK] Pulsar.Shared.dll deployed.")
    else:
        print(f"[WARN] Pulsar.Shared.dll not found at: {src_pulsar_dll}")
        print("[WARN] Plugin may fail to load — ensure Dependencies\\Pulsar.Shared.dll exists.")


# ---------------------------------------------------------------------------
# ENTRY POINT
# ---------------------------------------------------------------------------
if __name__ == "__main__":
    ensure_xml_exists()
    ver = get_version()

    # Update version in source BEFORE building — compiled binary and deployed
    # source will both contain the correct version string.
    update_config_version(ver)

    if run_build(ver):
        update_readme_version(ver)
        deploy(ver)
        print(f"\n>>> Build & Deploy complete: v{ver} @ {datetime.now().strftime('%H:%M:%S')}")
    else:
        print(f"\n[!] Build FAILED — deployment skipped. Version file rolled back.")
        try:
            parts = ver.split(".")
            rolled = ".".join(parts[:-1] + [str(int(parts[-1]) - 1)])
            with open(VERSION_FILE, "w") as f:
                f.write(rolled)
            update_config_version(rolled)  # keep Config.cs in sync with version.txt
            print(f"[INFO] Version rolled back to {rolled}.")
        except Exception:
            pass
