import os
import sys
import shutil
import zipfile
import subprocess
import xml.etree.ElementTree as ET
from datetime import datetime
import glob
import re

# --- CONFIGURATION ---
PROJECT_NAME = "mamba.PulsarSurveyorCompute"
PROJ_FILE = f"{PROJECT_NAME}.csproj"
# Standard path for VS 2022 Community. Adjust if using Professional or Enterprise.
MSBUILD_PATH = r"C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
OUT_DIR = os.path.join("bin", "Release", "net48")
PUBLISH_DIR = "build_staging"
ARCHIVE_DIR = "build_archive"
# Space Engineers local storage path for client-side plugins
TARGET_DIR = os.path.join(os.getenv('APPDATA'), r"SpaceEngineers\Storage\Pulsar_mamba.PulsarSurveyorCompute")

def cleanup_old_artifacts():
    """Removes any previous build ZIP files from the root directory."""
    pattern = os.path.join(os.getcwd(), f"{PROJECT_NAME}-v*.zip")
    for f in glob.glob(pattern):
        try:
            os.remove(f)
            print(f"[CLEANUP] Removed: {os.path.basename(f)}")
        except Exception as e:
            print(f"[CLEANUP] Could not remove {f}: {e}")

def get_version(auto_increment=False):
    """Manages versioning via a local version.txt file."""
    version_file = 'version.txt'
    if not os.path.exists(version_file):
        with open(version_file, "w") as f: f.write("1.0.0")
        return "1.0.0"

    with open(version_file, "r") as f:
        current_v = f.read().strip()
    
    # Increment the patch version (1.0.0 -> 1.0.1)
    parts = current_v.split('.')
    if len(parts) >= 3:
        parts[-1] = str(int(parts[-1]) + 1)
        suggested_v = ".".join(parts)
    else:
        suggested_v = current_v + ".1"

    if auto_increment:
        final_v = suggested_v
        print(f"[AUTO] Incrementing version: {current_v} -> {final_v}")
    else:
        print(f"\n--- VERSIONING ---")
        print(f"Current version: {current_v}")
        ui = input(f"Enter new version [{suggested_v}] (Enter to confirm): ").strip()
        final_v = ui if ui else suggested_v

    with open(version_file, "w") as f:
        f.write(final_v)
    
    return final_v

def run_build():
    """Triggers MSBuild to compile the solution in Release mode."""
    print("\n--- MSBUILD STARTING ---")
    if not os.path.exists(MSBUILD_PATH):
        print(f"[ERROR] MSBuild not found at: {MSBUILD_PATH}")
        return False
    
    # Executing Restore and Rebuild targets
    cmd = [MSBUILD_PATH, PROJ_FILE, "/t:Restore;Rebuild", "/p:Configuration=Release", "/v:minimal"]
    result = subprocess.run(cmd)
    return result.returncode == 0

def prepare_staging():
    """Filters build artifacts and prepares them for deployment."""
    print("\n--- PREPARING STAGING ---")
    if os.path.exists(PUBLISH_DIR):
        shutil.rmtree(PUBLISH_DIR)
    os.makedirs(PUBLISH_DIR)

    # Exclude game engine DLLs to keep the plugin package clean
    excluded_prefixes = ("Sandbox", "VRage", "System", "Microsoft")

    count = 0
    for root, dirs, files in os.walk(OUT_DIR):
        for file in files:
            if file.endswith((".dll", ".xml")):
                if any(file.startswith(p) for p in excluded_prefixes): 
                    continue
                
                src_path = os.path.join(root, file)
                shutil.copy(src_path, os.path.join(PUBLISH_DIR, file))
                count += 1
    
    print(f"[OK] Staging completed. {count} files prepared.")

def create_zip(version):
    """Archives staged files into a versioned ZIP."""
    zip_name = f"{PROJECT_NAME}-v{version}.zip"
    with zipfile.ZipFile(zip_name, 'w', zipfile.ZIP_DEFLATED) as zipf:
        for file in os.listdir(PUBLISH_DIR):
            zipf.write(os.path.join(PUBLISH_DIR, file), file)
    print(f"[OK] Created archive: {zip_name}")
    return zip_name

def deploy_and_archive(temp_zip, version):
    """Deploys the DLL to the SE Storage folder and moves the ZIP to the archive."""
    if not os.path.exists(ARCHIVE_DIR):
        os.makedirs(ARCHIVE_DIR)
    
    timestamp = datetime.now().strftime("%Y-%m-%d_%H%M")
    final_archive_name = f"{timestamp}_{PROJECT_NAME}_v{version}.zip"
    archive_path = os.path.join(ARCHIVE_DIR, final_archive_name)
    
    if not os.path.exists(TARGET_DIR):
        os.makedirs(TARGET_DIR)
    
    # Deploying only the main DLL for immediate use by the game/loader
    target_path = os.path.join(TARGET_DIR, f"{PROJECT_NAME}.dll")
    dll_source = os.path.join(PUBLISH_DIR, f"{PROJECT_NAME}.dll")
    
    try:
        shutil.copy(dll_source, target_path)
        print(f"[SUCCESS] Deployed to SE Storage: {target_path}")
        
        shutil.move(temp_zip, archive_path)
        print(f"[OK] Build moved to archive: {final_archive_name}")
    except PermissionError:
        print("[!] ERROR: Access denied. Ensure Space Engineers is not locking the file.")
    except Exception as e:
        print(f"[ERROR] Deployment or Archiving failed: {e}")

    # Maintain only the last 10 builds in the archive
    backups = sorted([os.path.join(ARCHIVE_DIR, f) for f in os.listdir(ARCHIVE_DIR)], key=os.path.getmtime)
    while len(backups) > 10:
        os.remove(backups.pop(0))

if __name__ == "__main__":
    cleanup_old_artifacts()
    
    # Check for auto-confirm flag
    is_auto = "-y" in sys.argv or "--yes" in sys.argv
    current_ver = get_version(auto_increment=is_auto)
    
    if run_build():
        prepare_staging()
        zip_file = create_zip(current_ver)
        deploy_and_archive(zip_file, current_ver)
        print("\n--- BUILD PROCESS FINISHED SUCCESSFULLY ---")
    else:
        print("\n[FAILED] Compilation failed. Check the MSBuild output above.")