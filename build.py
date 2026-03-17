# ---------------------------------------------------------------------------
# AUTOMATIC CONFIGURATION - TORCH and PULSAR PLUGIN BUILD & DEPLOY SCRIPT
# ---------------------------------------------------------------------------
# This script assumes a standard Torch server and Pulsar plugin project 
# structure and automates version management, building, and deployment to the
# Torch server or Pulsar AppData directory.
# It is designed to be run from the root of the plugin project directory.
#
#  Pulsar plugin projects should follow a standard structure:
#   - Root directory contains the .csproj file, version.txt, README.md, and the plugin manifest XML (if it exists).
#   - Source code is contained in a "Plugin" subdirectory.
#
#  Torch plugin projects should follow a standard structure:
#   - Root directory contains the .csproj file and manifest.xml.
#   - Build outputs are staged in a temporary directory, filtered to exclude 
#     game/engine assemblies (Torch, VRage, Sandbox), and packed into a ZIP archive.
#   - Automatic archiving of previous builds is handled in the build_archive folder.
#
# Project name and all paths are derived from the .csproj file.
# Nothing is hardcoded here except MSBuild path — adjust for your VS edition.
# ---------------------------------------------------------------------------

BUILD_VERSION = "1.2.8" # Mamba Build Tool - Config.cs Table Integration & Full XML Restore.

import os
import re
import shutil
import subprocess
import sys
import glob
import argparse
import zipfile
import xml.etree.ElementTree as ET
from datetime import datetime

# ============================================================
# CONFIGURATION
# ============================================================

# --- Build Type ---
# Set to: "PULSAR-CLIENT", "PULSAR-SERVER", or "TORCH-SERVER"
PLUGIN_TYPE = "PULSAR-CLIENT"

# --- Versioning Behavior ---
# If False, version files will be restored to previous state if MSBuild fails.
INCREMENT_ON_ERROR = False

# --- Project Identity ---
PROJECT_AUTHOR      = "mamba"
PROJECT_ID          = "mamba73/mamba.TorchDiscordSync.Plugin" # GitHub ID (Author/Repo)
PROJECT_DESCRIPTION = "Base template for Pulsar/Torch plugins with advanced automation."
PROJECT_TOOLTIP     = "Mamba Build Tool Template"

# --- Common Paths ---
LOG_DIR        = os.path.join("doc", "logs")
VERSION_FILE   = "version.txt"      # Used for Pulsar
MANIFEST_FILE  = "manifest.xml"     # Used for Torch
README_FILE    = "README.md"
CONFIG_CS      = os.path.join("Plugin", "Config", "MainConfig.cs")
PLUGIN_DIR     = "Plugin"

# MSBuild Path (Locked to v18 per user request)
MSBUILD_PATH   = r"C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\MSBuild.exe"

# --- Pulsar Specific ---
# Deploys to %APPDATA%\Pulsar\Legacy\Local\<ProjectName>
PULSAR_BASE_DIR = os.path.join(os.getenv('APPDATA'), r"Pulsar\Legacy\Local")

# --- Torch Specific ---
TORCH_TARGET_DIR = r"D:\g\torch-server\Plugins"
TORCH_OUT_DIR    = os.path.join("bin", "Release", "net48")
PUBLISH_DIR      = "build_staging"
ARCHIVE_DIR      = "build_archive"

# --- Terminal Colors ---
CLR_YELLOW = "\033[93m"
CLR_GREEN  = "\033[92m"
CLR_RED    = "\033[91m"
CLR_DEBUG  = "\033[94m"
CLR_RESET  = "\033[0m"

# ============================================================
# ERROR DICTIONARY (Mamba's Hint)
# ============================================================
ERROR_HINTS = {
    "CS1002": "Missing semicolon (;) somewhere in the line or before it.",
    "CS1513": "Missing closing brace (}). Check your scopes.",
    "CS0116": "Member declared outside of namespace or class. Move it inside.",
    "CS1001": "Identifier expected. Check your variable or class names.",
    "CS0246": "Type or namespace not found. Missing 'using' or reference?",
    "CS1061": "Type does not contain a definition for this member. Typo?",
    "CS0103": "The name does not exist in the current context. Missing variable declaration?"
}

# --- Pulsar XML Template (Standard GitHubPlugin format RESTORED) ---
XML_TEMPLATE = f"""<?xml version="1.0"?>
<PluginData xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:type="GitHubPlugin">
  <Id>{PROJECT_ID}</Id>
  
  <FriendlyName>{{PROJECT_NAME}}</FriendlyName>
  
  <Author>{PROJECT_AUTHOR}</Author>

  <Tooltip>{PROJECT_TOOLTIP}</Tooltip>

  <Description>{PROJECT_DESCRIPTION}</Description>
  
  <Version>{{PROJECT_VERSION}}</Version>

  <Commit>{{COMMIT_HASH}}</Commit>
</PluginData>"""

# ============================================================
# INITIALIZATION & STATE
# ============================================================

# State for Rollback
ORIGINAL_VERSION = None
VERSION_UPDATE_REPORT = []

# Auto-detect Project Name from .csproj
csproj_files = glob.glob("*.csproj")
if not csproj_files:
    print(f"{CLR_RED}[!] ERROR: No .csproj file found in root!{CLR_RESET}")
    sys.exit(1)

PROJ_FILE    = csproj_files[0]
PROJECT_NAME = os.path.splitext(PROJ_FILE)[0]

# Global Timestamps for Logging
TIMESTAMP      = datetime.now().strftime("%Y-%m-%d_%H%M%S")
MAIN_LOG_FILE  = os.path.join(LOG_DIR, f"{TIMESTAMP}_build.log")
ERROR_LOG_FILE = os.path.join(LOG_DIR, f"{TIMESTAMP}_build-ERROR.log")

# ============================================================
# UTILITIES & LOGGING
# ============================================================

def write_log_header(file_path, version):
    """Creates log directory and writes the mandatory build header."""
    os.makedirs(LOG_DIR, exist_ok=True)
    with open(file_path, "w", encoding="utf-8") as f:
        f.write(f"PROJECT:     {PROJECT_NAME}\n")
        f.write(f"VERSION:     {version}\n")
        f.write(f"BUILD TYPE:  {PLUGIN_TYPE}\n")
        f.write(f"LOG PATH:    {os.path.abspath(file_path)}\n")
        f.write(f"DATE:        {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}\n")
        f.write("-" * 60 + "\n\n")

def log(msg, level="INFO"):
    """Logs to file and outputs to terminal with colors."""
    clean_msg = re.sub(r'\033\[[0-9;]*m', '', msg)
    ts = datetime.now().strftime("%H:%M:%S")
    with open(MAIN_LOG_FILE, "a", encoding="utf-8") as f:
        f.write(f"[{ts}] [{level}] {clean_msg}\n")
    
    if level == "INFO":    print(f"{CLR_YELLOW}[INFO]{CLR_RESET} {msg}")
    elif level == "OK":      print(f"{CLR_GREEN}[OK]{CLR_RESET} {msg}")
    elif level == "ERROR":   print(f"{CLR_RED}[ERROR]{CLR_RESET} {msg}")
    elif level == "DEBUG":   print(f"{CLR_DEBUG}[DEBUG]{CLR_RESET} {msg}")

def print_version_report():
    """Outputs the status of all file version updates."""
    print(f"\n{CLR_YELLOW}Version Update Status:{CLR_RESET}")
    print(f"{'-' * 60}")
    for item in VERSION_UPDATE_REPORT:
        status_color = CLR_GREEN if "SUCCESS" in item['status'] else (CLR_RED if "ERROR" in item['status'] else CLR_YELLOW)
        print(f" {item['file']:<20} | {status_color}{item['status']:<10}{CLR_RESET} | {item['note']}")
    print(f"{'-' * 60}\n")

# ============================================================
# VERSION MANAGEMENT
# ============================================================

def update_config_version(v):
    """
    Updates the PluginVersion default value in Plugin/Models/Config.cs.

    Matches the line:
        public string PluginVersion { get; set; } = "X.X.X";

    Called BEFORE building so that the compiled binary and deployed source
    both contain the correct version string. Also called during rollback to
    keep Config.cs in sync with version.txt on build failure.
    """
    # ---------------------------------------------------------
    # MAMBA OBJAŠNJENJE:
    # Umjesto print() funkcija, sada koristimo VERSION_UPDATE_REPORT.append().
    # Tako ova funkcija komunicira sa završnom tablicom.
    # ---------------------------------------------------------
    if not os.path.exists(CONFIG_CS):
        VERSION_UPDATE_REPORT.append({'file': 'Config.cs', 'status': 'SKIPPED', 'note': 'File not found'})
        return

    try:
        with open(CONFIG_CS, "r", encoding="utf-8") as f:
            content = f.read()

        updated = re.sub(
            r'(public string PluginVersion \{ get; set; \} = ")[^"]+(")',
            lambda m: f'{m.group(1)}{v}{m.group(2)}',
            content
        )

        if updated == content:
            VERSION_UPDATE_REPORT.append({'file': 'Config.cs', 'status': 'SKIPPED', 'note': 'Pattern not found'})
        else:
            with open(CONFIG_CS, "w", encoding="utf-8") as f:
                f.write(updated)
            VERSION_UPDATE_REPORT.append({'file': 'Config.cs', 'status': 'SUCCESS', 'note': f'Set to {v}'})
    except Exception as e:
        VERSION_UPDATE_REPORT.append({'file': 'Config.cs', 'status': 'ERROR', 'note': str(e)})

def update_readme_version(new_v):
    """Mamba Table-Aware version updater for README.md. Supports Markdown tables."""
    if not os.path.exists(README_FILE):
        VERSION_UPDATE_REPORT.append({'file': 'README.md', 'status': 'SKIPPED', 'note': 'File not found'})
        return False
    try:
        with open(README_FILE, 'r', encoding='utf-8') as f:
            content = f.read()
        
        # This regex targets: **Version** | 2.4.39 or Version: 1.0, etc.
        patterns = [
            (r"(\*\*?Version\*\*?.*?)([0-9]+\.[0-9]+\.[0-9]+(\.[0-9]+)?)", rf"\g<1>{new_v}")
        ]
        
        updated_content = content
        total_changes = 0
        
        for pat, repl in patterns:
            updated_content, count = re.subn(pat, repl, updated_content, flags=re.IGNORECASE)
            total_changes += count
        
        if total_changes > 0:
            with open(README_FILE, 'w', encoding='utf-8') as f:
                f.write(updated_content)
            VERSION_UPDATE_REPORT.append({'file': 'README.md', 'status': 'SUCCESS', 'note': f'Updated {total_changes} match(es)'})
            return True
        else:
            VERSION_UPDATE_REPORT.append({'file': 'README.md', 'status': 'SKIPPED', 'note': 'No version pattern found'})
            return False
    except Exception as e:
        VERSION_UPDATE_REPORT.append({'file': 'README.md', 'status': 'ERROR', 'note': str(e)})
        return False

def get_current_raw_version():
    """Reads current version from source without side effects."""
    if PLUGIN_TYPE == "TORCH-SERVER":
        if not os.path.exists(MANIFEST_FILE): return "1.0.0"
        try:
            tree = ET.parse(MANIFEST_FILE)
            return tree.getroot().find('Version').text.strip()
        except: return "1.0.0"
    else:
        if not os.path.exists(VERSION_FILE): return "0.0.1"
        with open(VERSION_FILE, "r") as f: return f.read().strip()

def apply_version_to_files(new_v):
    """Writes the target version to all project files including XML manifest with FULL COMMIT HASH."""
    global VERSION_UPDATE_REPORT
    VERSION_UPDATE_REPORT = [] 

    # 1. Pulsar Version File
    if PLUGIN_TYPE != "TORCH-SERVER":
        try:
            with open(VERSION_FILE, "w") as f: f.write(new_v)
            VERSION_UPDATE_REPORT.append({'file': VERSION_FILE, 'status': 'SUCCESS', 'note': f'Set to {new_v}'})
        except Exception as e:
            VERSION_UPDATE_REPORT.append({'file': VERSION_FILE, 'status': 'ERROR', 'note': str(e)})

    # 2. Pulsar / Torch XML Manifest Sync (RESTORED FULL HASH SYNC)
    if PLUGIN_TYPE == "TORCH-SERVER":
        if os.path.exists(MANIFEST_FILE):
            try:
                ET.register_namespace('', "")
                tree = ET.parse(MANIFEST_FILE)
                tree.getroot().find('Version').text = new_v
                tree.write(MANIFEST_FILE, encoding='utf-8', xml_declaration=True)
                VERSION_UPDATE_REPORT.append({'file': MANIFEST_FILE, 'status': 'SUCCESS', 'note': f'Set to {new_v}'})
            except Exception as e:
                VERSION_UPDATE_REPORT.append({'file': MANIFEST_FILE, 'status': 'ERROR', 'note': str(e)})
        else:
            VERSION_UPDATE_REPORT.append({'file': MANIFEST_FILE, 'status': 'SKIPPED', 'note': 'Not found'})
    else:
        # PULSAR Mode: Sync Pulsar XML in root using CONFIGURATION and FULL COMMIT HASH
        try:
            pulsar_xml_name = f"{PROJECT_NAME}.xml"
            try:
                # Ovdje smo vratili generiranje punog hasha (40 znakova) umjesto [:8]
                commit_hash = subprocess.check_output(['git', 'rev-parse', 'HEAD'], stderr=subprocess.DEVNULL).decode().strip()
            except: commit_hash = "0000000000000000000000000000000000000000"
            
            content = XML_TEMPLATE.replace("{PROJECT_NAME}", PROJECT_NAME).replace("{PROJECT_VERSION}", new_v).replace("{COMMIT_HASH}", commit_hash)
            with open(pulsar_xml_name, "w", encoding="utf-8") as f:
                f.write(content)
            VERSION_UPDATE_REPORT.append({'file': pulsar_xml_name, 'status': 'SUCCESS', 'note': f'Full Hash Sync'})
        except Exception as e:
            VERSION_UPDATE_REPORT.append({'file': f"{PROJECT_NAME}.xml", 'status': 'ERROR', 'note': str(e)})

    # 3. README update
    update_readme_version(new_v)
    
    # 4. Config.cs update (Sada dodaje u tablicu)
    update_config_version(new_v)

def rollback_version():
    """Restores version files to original state if build fails."""
    if ORIGINAL_VERSION:
        log(f"Rolling back version to {ORIGINAL_VERSION} due to build failure...", "ERROR")
        apply_version_to_files(ORIGINAL_VERSION)
        print_version_report()

def get_version_logic(auto_inc=False):
    """Handles the versioning flow including auto-increment and manual input."""
    global ORIGINAL_VERSION
    current_v = get_current_raw_version()
    ORIGINAL_VERSION = current_v

    parts = current_v.split(".")
    try:
        parts[-1] = str(int(parts[-1]) + 1)
        suggested_v = ".".join(parts)
    except:
        suggested_v = current_v

    if auto_inc:
        target_v = suggested_v
    else:
        ans = input(f"Enter version [{suggested_v}]: ").strip()
        target_v = ans if ans else suggested_v

    apply_version_to_files(target_v)
    return target_v

# ============================================================
# BUILD PROCESS
# ============================================================

def run_msbuild(v):
    """Executes MSBuild with verbatim output and hint injection."""
    log(f"Starting MSBuild for {PLUGIN_TYPE}...", "INFO")
    parts = v.split('.')
    while len(parts) < 4: parts.append('0')
    file_v = '.'.join(parts[:4])

    cmd = [
        MSBUILD_PATH, PROJ_FILE, "/t:Restore;Rebuild", "/p:Configuration=Release",
        f"/p:Version={v}", f"/p:AssemblyVersion={file_v}", f"/p:FileVersion={file_v}", "/v:minimal", "/nologo"
    ]
    
    proc = subprocess.Popen(cmd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    errors = []
    
    for line in proc.stdout:
        clean_line = line.strip()
        if "error " in clean_line.lower():
            print(f"{CLR_RED}{clean_line}{CLR_RESET}")
            code_match = re.search(r"error (CS\d+):", clean_line)
            if code_match:
                code = code_match.group(1)
                hint = ERROR_HINTS.get(code, "No specific hint available.")
                print(f"   {CLR_DEBUG}>> MAMBA HINT [{code}]: {hint}{CLR_RESET}")
                errors.append(f"{clean_line} | HINT: {hint}")
            else:
                errors.append(clean_line)
        else:
            print(clean_line)

    proc.wait()
    if proc.returncode != 0:
        write_log_header(ERROR_LOG_FILE, v)
        with open(ERROR_LOG_FILE, "a", encoding="utf-8") as f:
            for err in errors: f.write(f"[BUILD ERROR] {err}\n")
        
        if not INCREMENT_ON_ERROR:
            rollback_version()
        return False
    return True

# ============================================================
# DEPLOYMENT
# ============================================================

def deploy_pulsar(v):
    """Pulsar specific: Manifest, Source Tree and DLL deployment."""
    log("Deploying to Pulsar AppData...", "INFO")
    target_dir = os.path.join(PULSAR_BASE_DIR, PROJECT_NAME)
    os.makedirs(target_dir, exist_ok=True)
    
    # DLL Deployment
    dll_src = os.path.join("bin", "Release", f"{PROJECT_NAME}.dll")
    if os.path.exists(dll_src):
        shutil.copy(dll_src, os.path.join(target_dir, f"{PROJECT_NAME}.dll"))
        log(f"DEPLOYED DLL: {dll_src} -> {target_dir}", "DEBUG")
    
    # XML Metadata Deployment (Copying the synced XML from root)
    # ---------------------------------------------------------
    # MAMBA OBJAŠNJENJE:
    # Ovdje kopiramo ranije napravljeni XML (koji ima 40-char hash i
    # dobru strukturu) iz root foldera direktno u AppData.
    # ---------------------------------------------------------
    pulsar_xml_name = f"{PROJECT_NAME}.xml"
    if os.path.exists(pulsar_xml_name):
        shutil.copy(pulsar_xml_name, os.path.join(target_dir, f"{PROJECT_NAME}.dll.xml"))
        log(f"DEPLOYED XML: {pulsar_xml_name} -> {target_dir}/{PROJECT_NAME}.dll.xml", "DEBUG")
    
    # Source Sync
    target_src = os.path.join(target_dir, PLUGIN_DIR)
    if os.path.exists(target_src): shutil.rmtree(target_src)
    shutil.copytree(PLUGIN_DIR, target_src)
    log(f"DEPLOYED SOURCE: {PLUGIN_DIR} -> {target_src}", "DEBUG")

def deploy_torch(v):
    """Torch specific: Assembly filtering, ZIP creation and archiving."""
    log("Staging Torch build...", "INFO")
    if os.path.exists(PUBLISH_DIR): shutil.rmtree(PUBLISH_DIR)
    os.makedirs(PUBLISH_DIR)

    excl_prefixes = ("Torch", "VRage", "Sandbox", "SQLite", "System.Data.SQLite")
    for root, _, files in os.walk(TORCH_OUT_DIR):
        for f in files:
            if f.endswith((".dll", ".xml", ".config")) and f.lower() != "manifest.xml":
                if any(f.startswith(p) for p in excl_prefixes): continue
                shutil.copy(os.path.join(root, f), os.path.join(PUBLISH_DIR, f))
                log(f"STAGING: {f}", "DEBUG")
    
    if os.path.exists(MANIFEST_FILE):
        shutil.copy(MANIFEST_FILE, os.path.join(PUBLISH_DIR, "manifest.xml"))
    
    zip_name = f"{PROJECT_NAME}-v{v}.zip"
    with zipfile.ZipFile(zip_name, 'w', zipfile.ZIP_DEFLATED) as z:
        for f in os.listdir(PUBLISH_DIR): z.write(os.path.join(PUBLISH_DIR, f), f)
    log(f"ZIP CREATED: {zip_name}", "OK")

    if os.path.exists(TORCH_TARGET_DIR):
        shutil.copy(zip_name, os.path.join(TORCH_TARGET_DIR, f"{PROJECT_NAME}.zip"))
        log(f"DEPLOYED TO TORCH: {TORCH_TARGET_DIR}", "OK")

    os.makedirs(ARCHIVE_DIR, exist_ok=True)
    shutil.move(zip_name, os.path.join(ARCHIVE_DIR, f"{TIMESTAMP}_{PROJECT_NAME}_v{v}.zip"))
    log(f"ARCHIVED: {ARCHIVE_DIR}", "DEBUG")

# ============================================================
# MAIN
# ============================================================

def main():
    parser = argparse.ArgumentParser(formatter_class=argparse.RawTextHelpFormatter, add_help=False)
    group = parser.add_argument_group("COMMANDS")
    group.add_argument("-y", "--yes", action="store_true", help="AUTOMATIC\nIncrements version and skips the prompt.\n ")
    group.add_argument("-h", "--help", action="help", help="HELP\nShow this screen.\n ")
    args, _ = parser.parse_known_args()

    # Initial Banner
    print(f"\n{CLR_YELLOW}====================================================")
    print(f"  MAMBA BUILD TOOL v{BUILD_VERSION} | {PROJECT_NAME} v{get_current_raw_version()}")
    print(f"  MODE: {PLUGIN_TYPE}")
    print(f"===================================================={CLR_RESET}")

    v = get_version_logic(auto_inc=args.yes)
    write_log_header(MAIN_LOG_FILE, v)

    if run_msbuild(v):
        if PLUGIN_TYPE == "TORCH-SERVER": deploy_torch(v)
        else: deploy_pulsar(v)
        
        print_version_report()
        print(f"{CLR_GREEN}>>> SUCCESS: v{v} build & deploy complete.{CLR_RESET}")
    else:
        sys.exit(1)

if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        print(f"\n{CLR_RED}[!] Build aborted by user.{CLR_RESET}")
        sys.exit(1)