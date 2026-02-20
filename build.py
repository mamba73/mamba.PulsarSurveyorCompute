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
MSBUILD_PATH = r"C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
OUT_DIR = os.path.join("bin", "Release", "net48")
PUBLISH_DIR = "build_staging"
ARCHIVE_DIR = "build_archive"
TARGET_DIR = os.path.join(os.getenv('APPDATA'), r"SpaceEngineers\Storage\Pulsar_mamba.PulsarSurveyorCompute")

def update_readme(version):
    """Updates the version number in README.md."""
    readme_path = "README.md"
    if not os.path.exists(readme_path):
        print("[README] File not found, skipping update.")
        return

    try:
        with open(readme_path, "r", encoding="utf-8") as f:
            content = f.read()

        # Regex to find **Version**: x.x.x
        new_content = re.sub(r"(\*\*Version\*\*:\s*)\d+\.\d+\.\d+", r"\g<1>" + version, content)

        with open(readme_path, "w", encoding="utf-8") as f:
            f.write(new_content)
        print(f"[README] Updated to version {version}")
    except Exception as e:
        print(f"[README] Error updating: {e}")

def cleanup_old_artifacts():
    pattern = os.path.join(os.getcwd(), f"{PROJECT_NAME}-v*.zip")
    for f in glob.glob(pattern):
        try:
            os.remove(f)
            print(f"[CLEANUP] Removed: {os.path.basename(f)}")
        except Exception as e:
            print(f"[CLEANUP] Could not remove {f}: {e}")

def get_version(auto_increment=False):
    version_file = 'version.txt'
    if not os.path.exists(version_file):
        with open(version_file, "w") as f: f.write("1.0.0")
        return "1.0.0"

    with open(version_file, "r") as f:
        current_v = f.read().strip()
    
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
    print("\n--- MSBUILD STARTING ---")
    if not os.path.exists(MSBUILD_PATH):
        print(f"[ERROR] MSBuild not found at: {MSBUILD_PATH}")
        return False
    cmd = [MSBUILD_PATH, PROJ_FILE, "/t:Restore;Rebuild", "/p:Configuration=Release", "/v:minimal"]
    result = subprocess.run(cmd)
    return result.returncode == 0

def prepare_staging():
    print("\n--- PREPARING STAGING ---")
    if os.path.exists(PUBLISH_DIR):
        shutil.rmtree(PUBLISH_DIR)
    os.makedirs(PUBLISH_DIR)
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
    zip_name = f"{PROJECT_NAME}-v{version}.zip"
    with zipfile.ZipFile(zip_name, 'w', zipfile.ZIP_DEFLATED) as zipf:
        for file in os.listdir(PUBLISH_DIR):
            zipf.write(os.path.join(PUBLISH_DIR, file), file)
    print(f"[OK] Created archive: {zip_name}")
    return zip_name

def deploy_and_archive(temp_zip, version):
    if not os.path.exists(ARCHIVE_DIR):
        os.makedirs(ARCHIVE_DIR)
    timestamp = datetime.now().strftime("%Y-%m-%d_%H%M")
    final_archive_name = f"{timestamp}_{PROJECT_NAME}_v{version}.zip"
    archive_path = os.path.join(ARCHIVE_DIR, final_archive_name)
    if not os.path.exists(TARGET_DIR):
        os.makedirs(TARGET_DIR)
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
    backups = sorted([os.path.join(ARCHIVE_DIR, f) for f in os.listdir(ARCHIVE_DIR)], key=os.path.getmtime)
    while len(backups) > 10:
        os.remove(backups.pop(0))

if __name__ == "__main__":
    cleanup_old_artifacts()
    is_auto = "-y" in sys.argv or "--yes" in sys.argv
    current_ver = get_version(auto_increment=is_auto)
    
    # Update README before build
    update_readme(current_ver)
    
    if run_build():
        prepare_staging()
        zip_file = create_zip(current_ver)
        deploy_and_archive(zip_file, current_ver)
        print("\n--- BUILD PROCESS FINISHED SUCCESSFULLY ---")
    else:
        print("\n[FAILED] Compilation failed. Check the MSBuild output above.")