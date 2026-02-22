# build.py
import os
import shutil
import subprocess
import sys
import glob
from datetime import datetime

# --- AUTOMATIC CONFIGURATION ---
csproj_files = glob.glob("*.csproj")
if not csproj_files:
    print("[!] ERROR: No .csproj file found!")
    sys.exit(1)

PROJ_FILE = csproj_files[0]
PROJECT_NAME = os.path.splitext(PROJ_FILE)[0]
MSBUILD_PATH = r"C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\MSBuild.exe"

# Destination: AppData/Pulsar/Legacy/Local/ProjectName/
BASE_TARGET_DIR = os.path.join(os.getenv('APPDATA'), r"Pulsar\Legacy\Local")
PROJECT_TARGET_DIR = os.path.join(BASE_TARGET_DIR, PROJECT_NAME)

XML_SOURCE = f"{PROJECT_NAME}.xml"
VERSION_FILE = "version.txt"

def ensure_xml_exists():
    if not os.path.exists(XML_SOURCE):
        print(f"[DEBUG] Creating default XML: {XML_SOURCE}")
        xml_content = f"""<?xml version="1.0" encoding="utf-8"?>
<PluginData xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:type="GitHubPlugin">
    <Id>{PROJECT_NAME}</Id>
    <FriendlyName>{PROJECT_NAME}</FriendlyName>
    <Author>mamba</Author>
    <Description>Advanced Space Engineers Plugin.</Description>
</PluginData>"""
        with open(XML_SOURCE, "w", encoding="utf-8") as f:
            f.write(xml_content)

def get_version():
    if not os.path.exists(VERSION_FILE):
        with open(VERSION_FILE, "w") as f: f.write("1.0.0")
        return "1.0.0"
    with open(VERSION_FILE, "r") as f:
        v = f.read().strip()
    try:
        parts = v.split('.')
        parts[-1] = str(int(parts[-1]) + 1)
        new_v = ".".join(parts)
    except:
        new_v = "1.0.1"
    with open(VERSION_FILE, "w") as f:
        f.write(new_v)
    return new_v

def run_build(v):
    print(f"\n>>> BUILDING VERSION {v}")
    if not os.path.exists(MSBUILD_PATH):
        print(f"[!] MSBuild not found at {MSBUILD_PATH}")
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
# ---------

def deploy(v):
    print(f"\n>>> DEPLOYING v{v} TO PROJECT FOLDER")
    dll_name = f"{PROJECT_NAME}.dll"
    xml_dest_name = f"{PROJECT_NAME}.dll.xml" 
    
    # Putanje
    src_dll = os.path.join("bin", "Release", dll_name)
    src_pulsar_dll = os.path.join("Dependencies", "Pulsar.Shared.dll")

    if not os.path.exists(PROJECT_TARGET_DIR):
        os.makedirs(PROJECT_TARGET_DIR)

    # 1. Kopiraj tvoj Plugin DLL (za svaki slučaj ako Pulsar odluči koristiti binarni mod)
    if os.path.exists(src_dll):
        shutil.copy(src_dll, os.path.join(PROJECT_TARGET_DIR, dll_name))

    # 2. Kopiraj XML
    if os.path.exists(XML_SOURCE):
        shutil.copy(XML_SOURCE, os.path.join(PROJECT_TARGET_DIR, xml_dest_name))

    # 3. Kopiraj Source CODE (za Pulsar on-the-fly kompilaciju)
    target_plugin_dir = os.path.join(PROJECT_TARGET_DIR, "Plugin")
    if os.path.exists("Plugin"):
        if os.path.exists(target_plugin_dir):
            shutil.rmtree(target_plugin_dir)
        shutil.copytree("Plugin", target_plugin_dir)

    # 4. KLJUČNO: Kopiraj Pulsar.Shared.dll u root projekta u AppData
    # Ovo omogućuje Pulsaru da pronađe IPulsarPlugin tijekom kompajliranja koda
    if os.path.exists(src_pulsar_dll):
        shutil.copy(src_pulsar_dll, os.path.join(PROJECT_TARGET_DIR, "Pulsar.Shared.dll"))
        print(f"[OK] Pulsar.Shared.dll deployed to AppData.")

# ---------
if __name__ == "__main__":
    ensure_xml_exists()
    ver = get_version()
    if run_build(ver):
        deploy(ver)
    print(f"\n>>> Finished: {datetime.now().strftime('%H:%M:%S')}")