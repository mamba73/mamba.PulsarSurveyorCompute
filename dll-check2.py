import os
import sys
import clr
import configparser
import re
import subprocess

# --- CONFIG ---

script_full_path = os.path.abspath(__file__)
script_dir = os.path.dirname(script_full_path)
script_base_name = os.path.splitext(os.path.basename(__file__))[0]
config_file = os.path.join(script_dir, "config_check.ini")

config = configparser.ConfigParser()


def load_config():
    defaults = {
        'DefaultPath': os.path.join(script_dir, "Dependencies"),
        'FilterKeywords': "Torch,Sandbox,VRage,SpaceEngineers,Discord",
        'LogDir': ".inspect",
        'VSCodePath': r"c:\dev\VSCode\bin\code.cmd"
    }

    updated = False

    if not os.path.exists(config_file):
        config['SETTINGS'] = defaults
        updated = True
    else:
        config.read(config_file)
        if 'SETTINGS' not in config:
            config['SETTINGS'] = {}
            updated = True
        for key, value in defaults.items():
            if not config.has_option('SETTINGS', key):
                config.set('SETTINGS', key, value)
                updated = True

    if updated:
        with open(config_file, 'w') as f:
            config.write(f)

    return {
        'path': config.get('SETTINGS', 'DefaultPath'),
        'keywords': config.get('SETTINGS', 'FilterKeywords').split(','),
        'log_dir': config.get('SETTINGS', 'LogDir'),
        'vscode_path': config.get('SETTINGS', 'VSCodePath')
    }


cfg = load_config()


# --- UTILITIES ---

def detect_keywords_from_directory(dll_list):
    roots = set()
    for dll in dll_list:
        name = os.path.splitext(dll)[0]
        root = name.split('.')[0]
        if len(root) > 2:
            roots.add(root)
    return sorted(roots)


def get_unique_log_path(directory, base_name):
    counter = 0
    full_path = os.path.join(directory, f"{base_name}.txt")
    while os.path.exists(full_path):
        counter += 1
        full_path = os.path.join(directory, f"{base_name}_{counter}.txt")
    return full_path


def exact_or_contains(value, term, exact):
    if not term:
        return True
    if exact:
        pattern = r'\b' + re.escape(term) + r'\b'
        return re.search(pattern, value, re.IGNORECASE)
    return term.lower() in value.lower()


def format_type_name(t):
    if t is None:
        return "void"

    name = t.Name
    mappings = {
        "Int64": "long",
        "UInt64": "ulong",
        "Int32": "int",
        "UInt32": "uint",
        "Single": "float",
        "Double": "double",
        "Boolean": "bool",
        "String": "string"
    }

    if name in mappings:
        return mappings[name]

    if '`' in name:
        base_name = name.split('`')[0]
        try:
            gen_args = t.GetGenericArguments()
            args_names = [format_type_name(a) for a in gen_args]
            return f"{base_name}<{', '.join(args_names)}>"
        except:
            return name

    return name


# --- DEPENDENCIES ---

def build_dependency_map(directory, dll_files):
    import System.Reflection as Reflection
    dep_map = {}

    for dll in dll_files:
        try:
            asm = Reflection.Assembly.LoadFrom(os.path.join(directory, dll))
            refs = [r.Name + ".dll" for r in asm.GetReferencedAssemblies()]
            dep_map[dll] = refs
        except:
            dep_map[dll] = []

    return dep_map


# --- INSPECT ---

def inspect_dll(dll_path, search_term=None, member_filter=None,
                ext_mode=False, deep_mode=False, exact_mode=False):

    results = []
    version = "Unknown"

    try:
        import System.Reflection as Reflection
        from System.Reflection import ReflectionTypeLoadException

        assembly = Reflection.Assembly.LoadFrom(os.path.abspath(dll_path))
        version = assembly.GetName().Version

        try:
            types = assembly.GetTypes()
        except ReflectionTypeLoadException as e:
            types = [t for t in e.Types if t is not None]

        for t in types:
            if not t.IsPublic:
                continue

            type_full = f"{t.Namespace}.{t.Name}"

            if not exact_or_contains(type_full, search_term, exact_mode):
                continue

            flags = (Reflection.BindingFlags.Public |
                     Reflection.BindingFlags.Instance |
                     Reflection.BindingFlags.Static |
                     Reflection.BindingFlags.FlattenHierarchy)

            temp_items = []

            if deep_mode:
                for f in t.GetFields(flags):
                    if exact_or_contains(f.Name, member_filter, exact_mode):
                        prefix = "[ST] " if f.IsStatic else ""
                        temp_items.append(f"  [F] {prefix}{format_type_name(f.FieldType)} {f.Name}")

            for m in t.GetMethods(flags):
                if m.IsSpecialName:
                    continue
                if exact_or_contains(m.Name, member_filter, exact_mode):
                    params = ", ".join(
                        f"{format_type_name(p.ParameterType)} {p.Name}"
                        for p in m.GetParameters()
                    )
                    prefix = "[ST] " if m.IsStatic else ""
                    temp_items.append(f"  - {prefix}{format_type_name(m.ReturnType)} {m.Name}({params})")

            if ext_mode or deep_mode:
                for p in t.GetProperties(flags):
                    if exact_or_contains(p.Name, member_filter, exact_mode):
                        prefix = "[ST] " if any(a.IsStatic for a in p.GetAccessors()) else ""
                        temp_items.append(f"  [P] {prefix}{format_type_name(p.PropertyType)} {p.Name}")

            if temp_items:
                t_type = "Struct" if t.IsValueType else "Class"
                base_info = f" : {t.BaseType.Name}" if t.BaseType and t.BaseType.Name != "Object" else ""
                results.append(f"\n[NS: {t.Namespace}] -> {t_type}: {t.Name}{base_info}")
                results.extend(temp_items)

    except:
        pass

    return version, results


# --- MAIN ---

def main():

    if "-h" in sys.argv or "--help" in sys.argv:
        print("""
.NET DLL Inspector v2.60
======================================================================

OPTION              DESCRIPTION
----------------------------------------------------------------------

-s <term>           Filter TYPE names (Namespace.TypeName)
-f <term>           Filter MEMBER names (methods, properties, fields)
-x                  Exact word match (no substring)
-e                  Include properties
-d                  Deep mode (fields + properties)
-a                  Scan ALL DLL files (ignore config keywords)
--deps              Show dependency graph
-y                  Use default path from config
-o                  Open generated log in VSCode
-h                  Show this help

EXAMPLES
----------------------------------------------------------------------

-s Math
    Matches: MathHelper, MyMathConstants

-s Math -x
    Matches only exact word "Math"

-s Math -f Add
    Types containing Math AND members containing Add
======================================================================
""")
        return

    ext_mode = "-e" in sys.argv
    deep_mode = "-d" in sys.argv
    exact_mode = "-x" in sys.argv
    scan_all = "-a" in sys.argv
    deps_mode = "--deps" in sys.argv
    use_default = "-y" in sys.argv
    open_vscode = "-o" in sys.argv

    search_term = sys.argv[sys.argv.index("-s") + 1] if "-s" in sys.argv and sys.argv.index("-s") + 1 < len(sys.argv) else None
    member_filter = sys.argv[sys.argv.index("-f") + 1] if "-f" in sys.argv and sys.argv.index("-f") + 1 < len(sys.argv) else None

    print("--- .NET DLL Inspector v2.60 ---")

    target_dir = os.path.abspath(cfg['path']) if use_default else input("Path: ").strip()

    if not os.path.isdir(target_dir):
        print("Invalid directory.")
        return

    all_dlls = [f for f in os.listdir(target_dir) if f.lower().endswith(".dll")]
    detected = detect_keywords_from_directory(all_dlls)
    print(f'Found Keywords: "{",".join(detected)}"')

    dll_files = all_dlls if scan_all else [
        f for f in all_dlls
        if any(k.lower() in f.lower() for k in cfg['keywords'])
    ]

    if deps_mode:
        dep_map = build_dependency_map(target_dir, dll_files)
        print("\nDEPENDENCIES:\n")
        for dll, refs in dep_map.items():
            print(dll)
            for r in refs:
                if r in dll_files:
                    print(f"  -> {r}")
        return

    log_dir_full = os.path.join(script_dir, cfg['log_dir'])
    if not os.path.exists(log_dir_full):
        os.makedirs(log_dir_full)

    clean_s = re.sub(r'[^\w]', '', search_term) if search_term else "All"
    clean_f = f"_f_{re.sub(r'[^\w]', '', member_filter)}" if member_filter else ""
    base_log_name = f"inspect_{clean_s}{clean_f}"
    log_path = get_unique_log_path(log_dir_full, base_log_name)

    total_matches = 0

    with open(log_path, "w", encoding="utf-8") as f:
        f.write(f"REPORT: {target_dir}\nSEARCH: {search_term} | FILTER: {member_filter}\n")
        f.write("=" * 60 + "\n")

        for index, dll in enumerate(dll_files, start=1):
            print(f"\r[{index}/{len(dll_files)}] Analyzing {dll[:30].ljust(30)}", end="", flush=True)

            version, matches = inspect_dll(
                os.path.join(target_dir, dll),
                search_term,
                member_filter,
                ext_mode,
                deep_mode,
                exact_mode
            )

            if matches:
                total_matches += 1
                f.write(f"\nFILE: {dll} (v{version})\n")
                f.write("-" * 60 + "\n")
                f.write("\n".join(matches) + "\n")

        if total_matches == 0:
            f.write("\nNo results found.\n")

    if total_matches == 0:
        print(f"\n\n[!] No results found. (Log: {os.path.basename(log_path)})")
    else:
        print(f"\n\nDONE! {total_matches} file(s) matched.")
        print(f"Results saved: {os.path.basename(log_path)}")

    if open_vscode:
        vscode_cmd = cfg['vscode_path']
        if os.path.exists(vscode_cmd):
            subprocess.run([vscode_cmd, log_path], shell=True)
        else:
            os.startfile(log_path)


if __name__ == "__main__":
    main()