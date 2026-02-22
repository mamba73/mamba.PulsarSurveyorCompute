# ==============================================================================
# MAMBA SYNC TOOL
# Version: 1.18.1
#
# Fully config-driven automation tool for:
# - Dev sync
# - Public update / release
# - ZIP & backup generation
# - Changelog handling
#
# ALL behavior is controlled via config_sync.ini
# NO hardcoded business logic.
#
# FIXES in 1.18.1 (over 1.18.0):
# - --zip whitelist: strict matching only (exact filename OR dir/ prefix).
#   No regex, no wildcards. What you list is what goes in.
# - --full-backup: includes .git, no filters, truly EVERYTHING.
# - DirtyGuard: replaced commit+reset with git stash to avoid HEAD confusion
#   when additional commits are made inside the guard.
# - DEFAULT_CONFIG whitelist cleaned up – no regex patterns.
# - Release branch existence check in --update / --release.
# ==============================================================================

import os
import sys
import re
import argparse
import subprocess
import configparser
import zipfile
from datetime import datetime
import xml.etree.ElementTree as ET

# ==============================================================================
# VERSION
# ==============================================================================
SCRIPT_VER = "1.18.1"

# ==============================================================================
# PATHS
# ==============================================================================
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
CONFIG_FILE = os.path.join(SCRIPT_DIR, "config_sync.ini")

# ==============================================================================
# CONFIGURATION
# ==============================================================================
DEFAULT_CONFIG = {
    "SETTINGS": {
        "LocalFolderName":           "CHANGE_ME",
        "RemoteProjectName":         "CHANGE_ME",
        "DefaultVersion":            "0.1.0",
        "DevRemote":                 "origin",
        "ReleaseRemote":             "origin",
        "DevBranch":                 "dev",
        "ReleaseBranch":             "master",
        "ManifestPath":              "manifest.xml",
        "ReadmePath":                "README.md",
        "ReadmeVersionPattern":      r"(Version[:\s]+)([0-9\.]+)",
        "ChangelogPath":             "CHANGELOG.md",
        "LogDir":                    "logs",
        "VSCodePath":                r"c:\dev\VSCode\bin\code.cmd",
        # Whitelist: exact filenames OR directory/ prefixes. NO wildcards, NO regex.
        "ReleaseWhiteList":          "Plugin/, .gitignore, CHANGELOG.md, LICENSE, manifest.xml, README.md",
        "BackupFormat":              "{date}_{time}_{type}_{project}_v{version}_{remote}_{branch}.zip",
        "BuildStagingDir":           "bin/Release",
        "BinaryStagingDir":          "build_staging",
        "EnableLoggingForZip":       "true",
        "EnableLoggingForFullBackup":"true",
    }
}

CONFIG_COMMENTS = """\
# ==============================================================================
# MAMBA SYNC TOOL CONFIGURATION
#
# DefaultVersion:
#   Project version fallback if manifest.xml is missing.
#
# ReadmeVersionPattern:
#   Regex used to locate version string in README.md.
#
# ReleaseWhiteList:
#   Files and folders included in LOCAL_ZIP.
#   Rules:
#     - "SomeFolder/"    -> includes the folder and ALL its contents recursively
#     - "README.md"      -> includes only that exact file (root level)
#   NO wildcards. NO regex. What you list is what goes in.
#   Example:
#     ReleaseWhiteList = Plugin/, .gitignore, CHANGELOG.md, LICENSE,
#                        mamba.TorchDiscordSync.csproj,
#                        mamba.TorchDiscordSync.sln,
#                        manifest.xml, README.md
#
# BackupFormat placeholders:
#   {date}     YYYY-MM-DD
#   {time}     HHMMSS
#   {type}     LOCAL_ZIP | FULL_BACKUP | SOURCE | BIN | RELEASE
#   {project}
#   {version}
#   {remote}
#   {branch}
# ==============================================================================

"""

# ==============================================================================
# LOGGING
# ==============================================================================
CURRENT_LOG_FILE = None

def log(msg, level="INFO"):
    ts   = datetime.now().strftime("%H:%M:%S")
    line = f"[{ts}] [{level}] {msg}"
    print(line)
    if CURRENT_LOG_FILE:
        try:
            with open(CURRENT_LOG_FILE, "a", encoding="utf-8") as f:
                f.write(line + "\n")
        except Exception:
            pass


# ==============================================================================
# CONFIG LOADER
# ==============================================================================
def load_and_sync_config():
    if not os.path.exists(CONFIG_FILE):
        cfg = configparser.ConfigParser()
        cfg.read_dict(DEFAULT_CONFIG)
        with open(CONFIG_FILE, "w", encoding="utf-8") as f:
            f.write(CONFIG_COMMENTS)
            cfg.write(f)
        print("Default config created. Please review config_sync.ini.")
        sys.exit(0)

    cfg = configparser.ConfigParser()
    cfg.read(CONFIG_FILE, encoding="utf-8")

    if "SETTINGS" not in cfg:
        cfg["SETTINGS"] = {}

    updated = False
    for k, v in DEFAULT_CONFIG["SETTINGS"].items():
        if k not in cfg["SETTINGS"]:
            cfg["SETTINGS"][k] = v
            updated = True

    if updated:
        with open(CONFIG_FILE, "w", encoding="utf-8") as f:
            f.write(CONFIG_COMMENTS)
            cfg.write(f)
        log("Config updated with new default keys.", "DEBUG")

    return cfg["SETTINGS"]


def cfgget(cfg, key, default=""):
    """Case-insensitive config get with fallback."""
    v = cfg.get(key.lower()) or cfg.get(key)
    return v if v else default


# ==============================================================================
# VERSION RESOLUTION
# ==============================================================================
def resolve_version(cfg):
    manifest = cfgget(cfg, "ManifestPath", "manifest.xml")
    if os.path.exists(manifest):
        try:
            tree = ET.parse(manifest)
            node = tree.getroot().find("Version")
            if node is not None and node.text:
                log(f"Version resolved from manifest.xml: {node.text.strip()}", "DEBUG")
                return node.text.strip()
        except Exception as e:
            log(f"Manifest read failed: {e}", "ERROR")
    ver = cfgget(cfg, "DefaultVersion", "0.1.0")
    log(f"Version resolved from DefaultVersion: {ver}", "DEBUG")
    return ver


# ==============================================================================
# README UPDATE
# ==============================================================================
def update_readme(cfg, version):
    path    = cfgget(cfg, "ReadmePath", "README.md")
    pattern = cfgget(cfg, "ReadmeVersionPattern", r"(Version[:\s]+)([0-9\.]+)")
    if not os.path.exists(path):
        log("README not found, skipping update.", "DEBUG")
        return
    with open(path, "r", encoding="utf-8") as f:
        txt = f.read()
    new_txt, count = re.subn(pattern, rf"\g<1>{version}", txt)
    if count == 0:
        log("README version pattern not found, applying generic fallback.", "DEBUG")
        new_txt = re.sub(r"\d+\.\d+\.\d+", version, txt, count=1)
    with open(path, "w", encoding="utf-8") as f:
        f.write(new_txt)
    log(f"README updated to version {version}.", "DEBUG")


# ==============================================================================
# WHITELIST MATCHING
# ==============================================================================
def parse_whitelist(cfg):
    """
    Returns a list of whitelist entries from config.
    Each entry is either:
      "SomeFolder/"  -> directory prefix (ends with /)
      "README.md"    -> exact root-level filename
    """
    raw = cfgget(cfg, "ReleaseWhiteList", "")
    return [e.strip() for e in raw.split(",") if e.strip()]


def whitelist_matches(rel_path, whitelist):
    """
    Strict whitelist check. No regex, no wildcards.

    rel_path : forward-slash path relative to source_dir root
               e.g. "Plugin/Subfolder/file.dll"  or  "README.md"

    Rules:
      "Plugin/"   -> matches any rel_path starting with "Plugin/"
      "README.md" -> matches only "README.md" at root level
    """
    for entry in whitelist:
        if entry.endswith("/"):
            # Directory match: file must be inside this directory
            if rel_path.startswith(entry):
                return True
        else:
            # Exact root-level filename
            if rel_path == entry:
                return True
    return False


# ==============================================================================
# ZIP CREATION
# ==============================================================================
def create_zip(source_dir, output_path, whitelist=None, include_git=False):
    """
    Create a ZIP from source_dir into output_path.

    whitelist   : list of entries from parse_whitelist().
                  If None -> include ALL files (no filter).
    include_git : if True, .git directory is included.
                  If False (default), .git is excluded.

    The output_path itself is NEVER included in the archive.
    """
    output_path_abs = os.path.abspath(output_path)
    source_dir_abs  = os.path.abspath(source_dir)
    log(f"Creating ZIP  : {output_path_abs}", "DEBUG")
    log(f"  Source      : {source_dir_abs}", "DEBUG")
    log(f"  Whitelist   : {whitelist if whitelist is not None else 'ALL (no filter)'}", "DEBUG")
    log(f"  Include .git: {include_git}", "DEBUG")

    with zipfile.ZipFile(output_path_abs, "w", zipfile.ZIP_DEFLATED) as z:
        for root, dirs, files in os.walk(source_dir_abs):
            # Prune .git in-place so os.walk never descends into it
            if not include_git:
                dirs[:] = [d for d in dirs if d != ".git"]

            for filename in files:
                full = os.path.join(root, filename)

                # Never include the output ZIP itself
                if os.path.abspath(full) == output_path_abs:
                    continue

                rel = os.path.relpath(full, source_dir_abs).replace("\\", "/")

                if whitelist is not None:
                    if not whitelist_matches(rel, whitelist):
                        continue

                z.write(full, rel)
                log(f"  + {rel}", "DEBUG")

    size_mb = os.path.getsize(output_path_abs) / (1024 * 1024)
    log(f"ZIP created: {output_path_abs} ({size_mb:.2f} MB)", "INFO")


# ==============================================================================
# BACKUP NAMING
# ==============================================================================
def backup_name(cfg, btype, version, remote=None, branch=None):
    fmt = cfgget(cfg, "BackupFormat",
                 "{date}_{time}_{type}_{project}_v{version}_{remote}_{branch}.zip")
    return fmt.format(
        date    = datetime.now().strftime("%Y-%m-%d"),
        time    = datetime.now().strftime("%H%M%S"),
        type    = btype,
        project = cfgget(cfg, "RemoteProjectName", "PROJECT"),
        version = version,
        remote  = remote or "LOCAL",
        branch  = branch or cfgget(cfg, "DevBranch", "dev"),
    )


# ==============================================================================
# GIT HELPERS
# ==============================================================================
def run(cmd, abort_on_error=True):
    log(f"EXEC: {cmd}", "DEBUG")
    res = subprocess.run(cmd, shell=True, text=True, capture_output=True, cwd=SCRIPT_DIR)
    if res.stdout.strip():
        log(res.stdout.strip(), "DEBUG")
    if res.stderr.strip():
        log(res.stderr.strip(), "DEBUG")
    if res.returncode != 0:
        log(f"Command failed (rc={res.returncode}).", "ERROR")
        if abort_on_error:
            sys.exit(1)
    return res.stdout.strip()


def run_ok(cmd):
    """Run command silently. Returns (success: bool, stdout: str)."""
    res = subprocess.run(cmd, shell=True, text=True, capture_output=True, cwd=SCRIPT_DIR)
    return res.returncode == 0, res.stdout.strip()


def is_dirty():
    _, out = run_ok("git status --porcelain")
    return bool(out.strip())


def current_branch():
    return run("git rev-parse --abbrev-ref HEAD")


def branch_exists_local(branch):
    ok, _ = run_ok(f"git rev-parse --verify {branch}")
    return ok


def branch_exists_remote(remote, branch):
    ok, _ = run_ok(f"git ls-remote --exit-code {remote} refs/heads/{branch}")
    return ok


# ==============================================================================
# DIRTY-TREE PROTECTION  (git stash)
# ==============================================================================
class DirtyGuard:
    """
    If the working tree is dirty before a destructive operation, stash all
    changes (including untracked files) and pop the stash on exit.

    Using git stash (NOT commit+reset) so that additional commits made inside
    the guard block do not confuse the restore logic.
    """
    STASH_MSG = "mamba-sync-guard-stash"

    def __init__(self, version, operation):
        self.version   = version
        self.operation = operation
        self.stashed   = False

    def __enter__(self):
        if is_dirty():
            log("Dirty working tree detected. Stashing changes...", "INFO")
            run(f'git stash push -u -m "{self.STASH_MSG}"')
            self.stashed = True
            log("Working tree stashed.", "DEBUG")
        else:
            log("Working tree is clean.", "DEBUG")
        return self

    def __exit__(self, exc_type, exc_val, exc_tb):
        if self.stashed:
            log("Restoring stashed changes...", "INFO")
            run("git stash pop", abort_on_error=False)
        return False  # do not suppress exceptions


# ==============================================================================
# GITHUB CLI CHECK
# ==============================================================================
def require_gh():
    ok, _ = run_ok("gh --version")
    if not ok:
        log("", "ERROR")
        log("===================================================================", "ERROR")
        log("  GitHub CLI (gh) is NOT installed or not found in PATH.", "ERROR")
        log("", "ERROR")
        log("  GitHub CLI is required for --release and --deploy.", "ERROR")
        log("  It allows this tool to create GitHub Releases and upload", "ERROR")
        log("  ZIP assets directly from the command line.", "ERROR")
        log("", "ERROR")
        log("  Download  : https://cli.github.com/", "ERROR")
        log("  After install, authenticate: gh auth login", "ERROR")
        log("===================================================================", "ERROR")
        sys.exit(1)
    log("GitHub CLI (gh) found.", "DEBUG")


# ==============================================================================
# CHANGELOG
# ==============================================================================
def get_log_since_last_tag():
    ok, last_tag = run_ok("git describe --tags --abbrev=0")
    if ok and last_tag:
        ok2, out = run_ok(f"git log {last_tag}..HEAD --oneline --no-merges")
    else:
        ok2, out = run_ok("git log --oneline --no-merges -30")
    if ok2 and out:
        return out.splitlines()
    return []


def update_changelog(cfg, version):
    path  = cfgget(cfg, "ChangelogPath", "CHANGELOG.md")
    lines = get_log_since_last_tag()
    if not lines:
        log("No new commits found for changelog.", "DEBUG")
        return

    entry  = f"## [{version}] - {datetime.now().strftime('%Y-%m-%d')}\n\n"
    entry += "\n".join(f"- {l}" for l in lines)
    entry += "\n\n"

    existing = ""
    if os.path.exists(path):
        with open(path, "r", encoding="utf-8") as f:
            existing = f.read()
        if f"## [{version}]" in existing:
            log(f"Changelog already contains [{version}], skipping.", "DEBUG")
            return

    with open(path, "w", encoding="utf-8") as f:
        f.write(entry + existing)
    log(f"Changelog updated: {path}", "INFO")


# ==============================================================================
# COMMIT MESSAGE INPUT
# ==============================================================================
COMMIT_CONVENTIONS = """\
  Commit conventions:
    feat:   New feature
    fix:    Bug fix
    refac:  Code cleanup / refactor
    perf:   Performance improvement
    docs:   Documentation changes
    chore:  Tooling / CI / build tasks

  Example: feat: Add auto-changelog generation
"""

def ask_commit_msg(default_msg):
    print(COMMIT_CONVENTIONS)
    msg = input(f"  Commit message [{default_msg}]: ").strip()
    if not msg:
        log("Empty commit message - operation aborted.", "INFO")
        sys.exit(0)
    return msg


# ==============================================================================
# OPERATIONS
# ==============================================================================

# -- DEV SYNC ------------------------------------------------------------------
def cmd_dev_sync(cfg, version, args):
    log("Starting DEV sync...", "INFO")
    update_readme(cfg, version)

    _, status = run_ok("git status --porcelain")
    log(f"Git status: {status if status else '[CLEAN]'}", "DEBUG")

    if not status:
        log("Nothing to commit. DEV sync aborted.", "INFO")
        return

    run("git add .")

    default_msg = f"[{version}] | auto commit dev sync"
    commit_msg  = default_msg if args.yes else ask_commit_msg(default_msg)

    run(f'git commit -m "{commit_msg}"')
    run(f"git push {cfgget(cfg, 'DevRemote', 'origin')} {cfgget(cfg, 'DevBranch', 'dev')}")
    log("DEV sync finished.", "INFO")


# -- ZIP (LOCAL_ZIP - whitelist only) ------------------------------------------
def cmd_zip(cfg, version):
    """
    Creates a ZIP containing ONLY the files/folders listed in ReleaseWhiteList.

    Strict matching:
      - "Plugin/"   -> entire Plugin directory and all subdirectories
      - "README.md" -> only README.md at root level

    No regex. No wildcards. Output goes into the project root. .git excluded.
    """
    whitelist = parse_whitelist(cfg)
    log(f"Whitelist entries ({len(whitelist)}): {whitelist}", "DEBUG")

    name = backup_name(cfg, "LOCAL_ZIP", version,
                       remote="LOCAL", branch=cfgget(cfg, "DevBranch", "dev"))
    out  = os.path.join(SCRIPT_DIR, name)

    create_zip(SCRIPT_DIR, out, whitelist=whitelist, include_git=False)
    log("ZIP finished.", "INFO")


# -- FULL BACKUP ---------------------------------------------------------------
def cmd_full_backup(cfg, version):
    """
    Creates a full backup ZIP of the ENTIRE project directory.

    - Source    : project directory (SCRIPT_DIR)
    - Output    : one level up (parent directory)
    - Filter    : NONE - everything is included
    - .git      : YES, included (complete repository backup)

    The output ZIP lives outside the project dir so it can never zip itself.
    """
    name       = backup_name(cfg, "FULL_BACKUP", version, remote="LOCAL", branch="DEV")
    parent_dir = os.path.dirname(SCRIPT_DIR)
    out        = os.path.join(parent_dir, name)

    log(f"Full backup -> {out}", "INFO")
    log("No whitelist filter. .git INCLUDED. Complete project snapshot.", "INFO")

    create_zip(SCRIPT_DIR, out, whitelist=None, include_git=True)
    log("Full backup finished.", "INFO")


# -- UPDATE --------------------------------------------------------------------
def cmd_update(cfg, version, args):
    """
    Pushes ONE clean commit to the public release branch.
    Dev history is NOT exposed (squash merge).
    No ZIPs are created or uploaded.

    Flow:
      1. Stash dirty tree if needed
      2. Update README + changelog on dev
      3. Commit those changes to dev
      4. Checkout release branch (create if missing)
      5. Squash merge from dev -> single clean commit
      6. Push release branch
      7. Return to dev branch
      8. Pop stash (DirtyGuard)
    """
    dev_branch     = cfgget(cfg, "DevBranch",     "dev")
    release_branch = cfgget(cfg, "ReleaseBranch", "master")
    dev_remote     = cfgget(cfg, "DevRemote",     "origin")
    release_remote = cfgget(cfg, "ReleaseRemote", "origin")
    orig_branch    = current_branch()

    with DirtyGuard(version, "update"):
        # Update metadata files on dev branch
        update_readme(cfg, version)
        update_changelog(cfg, version)

        _, s = run_ok("git status --porcelain")
        if s:
            run("git add .")
            run(f'git commit -m "[{version}] | readme + changelog update"')

        # Get commit message for the public release commit
        default_msg = f"[{version}] | public update"
        commit_msg  = default_msg if args.yes else ask_commit_msg(default_msg)

        # Checkout release branch
        log(f"Switching to {release_branch}...", "INFO")
        if branch_exists_local(release_branch):
            run(f"git checkout {release_branch}")
        elif branch_exists_remote(release_remote, release_branch):
            run(f"git checkout -b {release_branch} {release_remote}/{release_branch}")
        else:
            log(f"Release branch '{release_branch}' does not exist locally or remotely.", "ERROR")
            log(f"Create it: git checkout -b {release_branch} && git push -u {release_remote} {release_branch}", "ERROR")
            sys.exit(1)

        # Squash-merge dev -> one clean commit on release branch
        log(f"Squash-merging from {dev_branch}...", "INFO")
        run(f"git merge --squash {dev_branch}")
        run(f'git commit -m "{commit_msg}"')

        # Push
        log(f"Pushing to {release_remote}/{release_branch}...", "INFO")
        run(f"git push {release_remote} {release_branch}")

        # Return to original branch
        log(f"Returning to {orig_branch}...", "INFO")
        run(f"git checkout {orig_branch}")

    log("UPDATE finished.", "INFO")


# -- RELEASE -------------------------------------------------------------------
def cmd_release(cfg, version, args):
    """
    Same as --update, plus:
      - Creates SOURCE ZIP (whitelist only)
      - Creates BIN ZIP (BinaryStagingDir contents)
      - Uploads both to a new GitHub Release (tag = v{version})

    Requires GitHub CLI (gh). Aborts if not found.
    """
    require_gh()

    release_remote = cfgget(cfg, "ReleaseRemote", "origin")
    release_branch = cfgget(cfg, "ReleaseBranch", "master")
    whitelist      = parse_whitelist(cfg)
    bin_dir        = cfgget(cfg, "BinaryStagingDir", "build_staging")

    # Run the update first (squash commit to public master)
    cmd_update(cfg, version, args)

    # Source ZIP (whitelist only)
    src_name = backup_name(cfg, "SOURCE", version,
                           remote=release_remote, branch=release_branch)
    src_path = os.path.join(SCRIPT_DIR, src_name)
    log("Creating SOURCE ZIP (whitelist)...", "INFO")
    create_zip(SCRIPT_DIR, src_path, whitelist=whitelist, include_git=False)

    # Binary ZIP
    bin_path_abs = os.path.join(SCRIPT_DIR, bin_dir)
    bin_zip = None
    if os.path.isdir(bin_path_abs):
        bin_name = backup_name(cfg, "BIN", version,
                               remote=release_remote, branch=release_branch)
        bin_zip  = os.path.join(SCRIPT_DIR, bin_name)
        log(f"Creating BIN ZIP from {bin_dir}...", "INFO")
        create_zip(bin_path_abs, bin_zip, whitelist=None, include_git=False)
    else:
        log(f"Binary staging dir '{bin_dir}' not found - skipping BIN ZIP.", "INFO")

    # GitHub Release
    tag = f"v{version}"
    log(f"Creating GitHub Release: {tag}", "INFO")

    upload_files = f'"{src_path}"'
    if bin_zip:
        upload_files += f' "{bin_zip}"'

    run(f'gh release create {tag} {upload_files} '
        f'--title "Release {tag}" '
        f'--notes "Release {tag}"')

    log("RELEASE finished.", "INFO")


# -- DEPLOY (DESTRUCTIVE) ------------------------------------------------------
def cmd_deploy(cfg, version, args):
    """
    DESTRUCTIVE: Replaces public master history with a single clean snapshot.

    Uses git commit-tree to create a fully orphan commit (no parent commits)
    from the current dev tree, then force-pushes it to the release branch.
    Zero dev history is present on the remote after this operation.

    Flow:
      1. Confirm (unless -y)
      2. Stash dirty tree if needed
      3. Get the tree hash of current dev HEAD
      4. Create an orphan commit (commit-tree with no -p flag)
      5. Force-push that commit to the release branch
      6. Return to dev, pop stash
    """
    require_gh()

    dev_branch     = cfgget(cfg, "DevBranch",     "dev")
    release_branch = cfgget(cfg, "ReleaseBranch", "master")
    release_remote = cfgget(cfg, "ReleaseRemote", "origin")
    orig_branch    = current_branch()

    if not args.yes:
        print()
        print("  WARNING: --deploy will DESTROY the public master history.")
        print(f"     Remote : {release_remote}")
        print(f"     Branch : {release_branch}")
        print("     This cannot be undone on the remote.")
        print()
        ans = input("     Type YES to confirm: ").strip()
        if ans != "YES":
            log("Deploy aborted by user.", "INFO")
            sys.exit(0)

    default_msg = f"[{version}] | deploy snapshot"
    commit_msg  = default_msg if args.yes else ask_commit_msg(default_msg)

    with DirtyGuard(version, "deploy"):
        log("Resolving dev tree hash...", "DEBUG")
        dev_tree = run(f"git rev-parse {dev_branch}^{{tree}}")
        if not dev_tree:
            log("Could not resolve dev tree hash. Aborting.", "ERROR")
            sys.exit(1)
        log(f"Dev tree: {dev_tree}", "DEBUG")

        # Create an orphan commit: no -p means no parent -> zero history
        log("Creating orphan deploy commit...", "INFO")
        new_commit = run(f'git commit-tree {dev_tree} -m "{commit_msg}"')
        log(f"New orphan commit: {new_commit}", "DEBUG")

        # Point the local release branch ref at the new orphan commit
        run(f"git update-ref refs/heads/{release_branch} {new_commit}")

        # Force push - rewrites remote history
        log(f"Force-pushing to {release_remote}/{release_branch}...", "INFO")
        run(f"git push --force {release_remote} {release_branch}")

    log("DEPLOY finished. Public master is now a clean single-commit snapshot.", "INFO")
    log("Zero dev history is present on the remote.", "INFO")


# ==============================================================================
# MAIN
# ==============================================================================
def main():
    global CURRENT_LOG_FILE

    cfg = load_and_sync_config()

    # MANDATORY STARTUP BANNER - printed before argparse, always
    print("\n====================================================")
    print(f"  MAMBA SYNC TOOL v{SCRIPT_VER} | {cfgget(cfg, 'RemoteProjectName', 'PROJECT')}")
    print("====================================================\n")

    # LOG SETUP
    log_dir = cfgget(cfg, "LogDir", "logs")
    os.makedirs(os.path.join(SCRIPT_DIR, log_dir), exist_ok=True)
    CURRENT_LOG_FILE = os.path.join(
        SCRIPT_DIR, log_dir,
        f"{datetime.now().strftime('%Y-%m-%d_%H%M%S')}.log"
    )
    log(f"Log file: {CURRENT_LOG_FILE}", "DEBUG")

    # ARGS
    parser = argparse.ArgumentParser(
        description="MAMBA SYNC TOOL - git automation",
        formatter_class=argparse.RawTextHelpFormatter,
    )
    parser.add_argument("--zip",         action="store_true",
                        help="Create local whitelist ZIP only (no git operations)")
    parser.add_argument("--full-backup", action="store_true",
                        help="Create full project backup in parent dir (.git included, no filter)")
    parser.add_argument("--update",      action="store_true",
                        help="Push ONE clean commit to public master (squash merge, no history leak)")
    parser.add_argument("--release",     action="store_true",
                        help="--update + source ZIP + bin ZIP + GitHub Release upload")
    parser.add_argument("--deploy",      action="store_true",
                        help="DESTRUCTIVE: replace public master with single orphan commit")
    parser.add_argument("-y", "--yes",   action="store_true",
                        help="Automatic mode (skip all prompts, use default messages)")
    args = parser.parse_args()

    version = resolve_version(cfg)
    log(f"Project version: {version}", "DEBUG")

    # DISPATCH
    if args.full_backup:
        cmd_full_backup(cfg, version)

    elif args.zip:
        cmd_zip(cfg, version)

    elif args.update:
        cmd_update(cfg, version, args)

    elif args.release:
        cmd_release(cfg, version, args)

    elif args.deploy:
        cmd_deploy(cfg, version, args)

    else:
        # No flag = default dev sync
        cmd_dev_sync(cfg, version, args)


if __name__ == "__main__":
    main()