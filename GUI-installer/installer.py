from __future__ import annotations

import os
import queue
import re
import shutil
import sys
import threading
from pathlib import Path
from typing import Iterable, Optional

import customtkinter as ctk
from tkinter import filedialog, messagebox


APP_TITLE = "SCrawler Plugin Installer"
PLUGIN_PATTERN = re.compile(r"^SCrawler\.Plugin\..+\.dll$", re.IGNORECASE)
PROVIDER_DLL = "SCrawler.PluginProvider.dll"


def unique_paths(paths: Iterable[Path]) -> list[Path]:
    seen: set[str] = set()
    result: list[Path] = []
    for path in paths:
        try:
            resolved = str(path.resolve())
        except Exception:
            resolved = str(path)
        key = resolved.lower()
        if key in seen:
            continue
        seen.add(key)
        result.append(path)
    return result


def safe_iterdir(path: Path) -> list[Path]:
    try:
        return list(path.iterdir())
    except Exception:
        return []


def plugin_display_name(dll: Path) -> str:
    name = dll.stem
    prefix = "SCrawler.Plugin."
    if name.lower().startswith(prefix.lower()):
        return name[len(prefix) :]
    return name


def runtime_locations() -> list[Path]:
    locations: list[Path] = []

    script_dir = Path(__file__).resolve().parent
    locations.append(script_dir)
    locations.append(script_dir.parent)
    locations.append(Path.cwd())

    if getattr(sys, "frozen", False):
        exe_dir = Path(sys.executable).resolve().parent
        locations.append(exe_dir)
        locations.append(exe_dir.parent)

    meipass = getattr(sys, "_MEIPASS", None)
    if meipass:
        locations.append(Path(meipass))

    return unique_paths(locations)


def release_roots() -> list[Path]:
    candidates: list[Path] = []
    for base in runtime_locations():
        candidates.append(base / "releases")
        candidates.append(base.parent / "releases")

    roots = []
    for path in unique_paths(candidates):
        if path.is_dir():
            roots.append(path)
    return roots


def discover_plugin_dlls() -> list[Path]:
    by_name: dict[str, Path] = {}

    for root in release_roots():
        for dll in sorted(root.glob("*/*.dll")):
            if not dll.is_file():
                continue
            if dll.name.lower() == PROVIDER_DLL.lower():
                continue
            if not PLUGIN_PATTERN.match(dll.name):
                continue

            key = dll.name.lower()
            if key not in by_name:
                by_name[key] = dll

    return sorted(by_name.values(), key=lambda p: p.name.lower())


def registry_install_locations() -> list[Path]:
    if os.name != "nt":
        return []

    try:
        import winreg  # pylint: disable=import-error
    except Exception:
        return []

    roots = [
        (winreg.HKEY_CURRENT_USER, r"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
        (winreg.HKEY_LOCAL_MACHINE, r"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
        (winreg.HKEY_LOCAL_MACHINE, r"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
    ]

    results: list[Path] = []
    for hive, subkey in roots:
        try:
            with winreg.OpenKey(hive, subkey) as key:
                index = 0
                while True:
                    try:
                        name = winreg.EnumKey(key, index)
                    except OSError:
                        break
                    index += 1

                    try:
                        with winreg.OpenKey(key, name) as app_key:
                            display_name = str(_reg_get_value(winreg, app_key, "DisplayName") or "")
                            if "scrawler" not in display_name.lower():
                                continue

                            install_location = _reg_get_value(winreg, app_key, "InstallLocation")
                            display_icon = _reg_get_value(winreg, app_key, "DisplayIcon")

                            if install_location:
                                results.append(Path(str(install_location).strip('"')))

                            if display_icon:
                                icon_path = str(display_icon).split(",")[0].strip('"')
                                if icon_path:
                                    p = Path(icon_path)
                                    if p.suffix.lower() == ".exe":
                                        results.append(p.parent)
                                    else:
                                        results.append(p)
                    except Exception:
                        continue
        except Exception:
            continue

    return unique_paths(results)


def _reg_get_value(winreg_module, key, name: str):
    try:
        value, _ = winreg_module.QueryValueEx(key, name)
        return value
    except OSError:
        return None


def desktop_documents_bases() -> list[Path]:
    bases: list[Path] = []

    home = Path.home()
    bases.extend(
        [
            home / "Desktop",
            home / "Documents",
        ]
    )

    user_profile = os.getenv("USERPROFILE")
    if user_profile:
        up = Path(user_profile)
        bases.extend([up / "Desktop", up / "Documents"])

    one_drive = os.getenv("OneDrive")
    if one_drive:
        od = Path(one_drive)
        bases.extend([od / "Desktop", od / "Documents"])

    one_drive_consumer = os.getenv("OneDriveConsumer")
    if one_drive_consumer:
        odc = Path(one_drive_consumer)
        bases.extend([odc / "Desktop", odc / "Documents"])

    return [p for p in unique_paths(bases) if p.exists() and p.is_dir()]


def search_bases() -> list[Path]:
    bases: list[Path] = []

    env_candidates = [
        os.getenv("SCRAWLER_PATH"),
        os.getenv("SCRAWLER_DIR"),
        os.getenv("SCRAWLER_HOME"),
    ]
    for item in env_candidates:
        if item:
            bases.append(Path(item))

    home = Path.home()
    bases.extend(
        [
            home,
            home / "Downloads",
        ]
    )
    bases.extend(desktop_documents_bases())

    for env_key in ("ProgramFiles", "ProgramFiles(x86)", "LOCALAPPDATA", "APPDATA"):
        value = os.getenv(env_key)
        if value:
            bases.append(Path(value))

    bases.extend(runtime_locations())
    bases.extend(registry_install_locations())

    existing = [p for p in unique_paths(bases) if p.exists() and p.is_dir()]
    return existing


def is_desktop_or_documents_path(path: Path) -> bool:
    lowered = str(path).lower()
    if lowered.endswith("\\desktop") or lowered.endswith("\\documents"):
        return True
    return "\\onedrive\\desktop" in lowered or "\\onedrive\\documents" in lowered


def recursive_scrawler_dirs(base: Path, max_depth: int = 4, max_nodes: int = 6000) -> list[Path]:
    results: list[Path] = []
    queue_: list[tuple[Path, int]] = [(base, 0)]
    scanned = 0

    while queue_ and scanned < max_nodes:
        current, depth = queue_.pop(0)
        scanned += 1

        if (current / "SCrawler.exe").exists():
            results.append(current)

        for child in safe_iterdir(current):
            if not child.is_dir():
                continue

            name = child.name.lower()
            if "scrawler" in name:
                results.append(child)
            elif (child / "SCrawler.exe").exists():
                results.append(child)

            if depth + 1 >= max_depth:
                continue

            if name.startswith("."):
                continue

            queue_.append((child, depth + 1))

    return unique_paths(results)


def candidate_dirs(base: Path) -> list[Path]:
    candidates: list[Path] = [base]

    for child in safe_iterdir(base):
        if not child.is_dir():
            continue
        lname = child.name.lower()
        if "scrawler" in lname:
            candidates.append(child)

    for child in safe_iterdir(base):
        if not child.is_dir():
            continue
        lname = child.name.lower()
        if "portable" in lname or "apps" in lname or "programs" in lname:
            for grand in safe_iterdir(child):
                if grand.is_dir() and "scrawler" in grand.name.lower():
                    candidates.append(grand)

    return unique_paths(candidates)


def evaluate_candidate(path: Path) -> tuple[int, Optional[Path]]:
    score = 0
    plugins_dir: Optional[Path] = None

    if path.name.lower() == "plugins":
        plugins_dir = path
        score += 6
        if (path.parent / "SCrawler.exe").exists():
            score += 10
        if any(PLUGIN_PATTERN.match(p.name) for p in path.glob("*.dll")):
            score += 4

    if (path / "SCrawler.exe").exists():
        score += 12
        plugins_dir = path / "Plugins"
        if (path / "Plugins").exists():
            score += 5
        elif (path / "plugins").exists():
            score += 5
            plugins_dir = path / "plugins"

    for candidate_name in ("Plugins", "plugins"):
        plugin_subdir = path / candidate_name
        if not plugin_subdir.exists():
            continue
        score += 5
        plugins_dir = plugin_subdir
        if any(PLUGIN_PATTERN.match(p.name) for p in plugin_subdir.glob("*.dll")):
            score += 4

    if "scrawler" in path.name.lower():
        score += 2

    if plugins_dir and plugins_dir.exists():
        score += 3

    return score, plugins_dir


def normalize_target_plugins_dir(raw: str) -> Optional[Path]:
    value = raw.strip().strip('"')
    if not value:
        return None

    candidate = Path(value)
    if candidate.is_file() and candidate.name.lower() == "scrawler.exe":
        return candidate.parent / "Plugins"

    if candidate.name.lower() == "plugins":
        return candidate

    if (candidate / "SCrawler.exe").exists():
        return candidate / "Plugins"

    if candidate.exists() and candidate.is_dir():
        return candidate

    if candidate.suffix.lower() == ".exe":
        return candidate.parent / "Plugins"

    return candidate


def detect_scrawler_plugins_dir() -> Optional[Path]:
    env_plugins = os.getenv("SCRAWLER_PLUGINS_DIR")
    if env_plugins:
        env_target = normalize_target_plugins_dir(env_plugins)
        if env_target:
            return env_target

    best_score = -1
    best_plugins_dir: Optional[Path] = None
    checked: set[str] = set()

    for base in search_bases():
        candidates = candidate_dirs(base)
        if is_desktop_or_documents_path(base):
            candidates.extend(recursive_scrawler_dirs(base))

        for candidate in unique_paths(candidates):
            try:
                key = str(candidate.resolve()).lower()
            except Exception:
                key = str(candidate).lower()
            if key in checked:
                continue
            checked.add(key)

            score, plugins_dir = evaluate_candidate(candidate)
            if score <= best_score or plugins_dir is None:
                continue

            best_score = score
            best_plugins_dir = plugins_dir

    return best_plugins_dir


class InstallerApp(ctk.CTk):
    def __init__(self) -> None:
        super().__init__()
        self.title(APP_TITLE)
        self.geometry("820x620")
        self.minsize(760, 560)

        ctk.set_appearance_mode("system")
        ctk.set_default_color_theme("blue")

        self.plugins: list[Path] = []
        self.plugin_vars: dict[str, ctk.BooleanVar] = {}
        self.target_var = ctk.StringVar(value="")
        self.status_var = ctk.StringVar(value="Ready")
        self.log_queue: "queue.Queue[str]" = queue.Queue()
        self.worker_lock = threading.Lock()

        self._build_ui()
        self.refresh_plugins()
        self.start_auto_detect()
        self.after(120, self._drain_logs)

    def _build_ui(self) -> None:
        self.grid_columnconfigure(0, weight=1)
        self.grid_rowconfigure(3, weight=1)

        title = ctk.CTkLabel(
            self,
            text="SCrawler Plugin Installer",
            font=ctk.CTkFont(size=24, weight="bold"),
        )
        title.grid(row=0, column=0, sticky="w", padx=18, pady=(16, 8))

        path_frame = ctk.CTkFrame(self)
        path_frame.grid(row=1, column=0, sticky="ew", padx=16, pady=(0, 10))
        path_frame.grid_columnconfigure(1, weight=1)

        ctk.CTkLabel(path_frame, text="SCrawler Plugins Folder").grid(
            row=0, column=0, sticky="w", padx=12, pady=10
        )
        self.path_entry = ctk.CTkEntry(path_frame, textvariable=self.target_var)
        self.path_entry.grid(row=0, column=1, sticky="ew", padx=(0, 8), pady=10)

        browse_btn = ctk.CTkButton(path_frame, text="Browse", width=90, command=self.browse_target)
        browse_btn.grid(row=0, column=2, padx=(0, 8), pady=10)

        plugins_frame = ctk.CTkFrame(self)
        plugins_frame.grid(row=2, column=0, sticky="nsew", padx=16, pady=(0, 10))
        plugins_frame.grid_columnconfigure(0, weight=1)
        plugins_frame.grid_rowconfigure(1, weight=1)

        self.plugins_label = ctk.CTkLabel(plugins_frame, text="Available Plugins: 0")
        self.plugins_label.grid(row=0, column=0, sticky="w", padx=12, pady=(10, 6))

        options_frame = ctk.CTkFrame(plugins_frame, fg_color="transparent")
        options_frame.grid(row=0, column=1, sticky="e", padx=(0, 12), pady=(8, 2))

        select_all_btn = ctk.CTkButton(options_frame, text="Select All", width=90, command=self.select_all_plugins)
        select_all_btn.grid(row=0, column=0, padx=(0, 6), pady=2)

        clear_all_btn = ctk.CTkButton(options_frame, text="Clear All", width=90, command=self.clear_all_plugins)
        clear_all_btn.grid(row=0, column=1, padx=0, pady=2)

        self.plugins_scroll = ctk.CTkScrollableFrame(plugins_frame, height=170)
        self.plugins_scroll.grid(row=1, column=0, columnspan=2, sticky="nsew", padx=12, pady=(0, 12))
        self.plugins_scroll.grid_columnconfigure(0, weight=1)

        log_frame = ctk.CTkFrame(self)
        log_frame.grid(row=3, column=0, sticky="nsew", padx=16, pady=(0, 10))
        log_frame.grid_columnconfigure(0, weight=1)
        log_frame.grid_rowconfigure(1, weight=1)

        ctk.CTkLabel(log_frame, text="Activity").grid(row=0, column=0, sticky="w", padx=12, pady=(10, 6))
        self.log_text = ctk.CTkTextbox(log_frame)
        self.log_text.grid(row=1, column=0, sticky="nsew", padx=12, pady=(0, 12))
        self.log_text.configure(state="disabled")

        action_frame = ctk.CTkFrame(self)
        action_frame.grid(row=4, column=0, sticky="ew", padx=16, pady=(0, 12))
        action_frame.grid_columnconfigure(0, weight=1)

        self.install_btn = ctk.CTkButton(action_frame, text="Install Selected Plugins", command=self.install_all)
        self.install_btn.grid(row=0, column=0, sticky="w", padx=12, pady=10)

        refresh_btn = ctk.CTkButton(action_frame, text="Refresh Plugin List", width=150, command=self.refresh_plugins)
        refresh_btn.grid(row=0, column=1, sticky="e", padx=12, pady=10)

        status = ctk.CTkLabel(self, textvariable=self.status_var, anchor="w")
        status.grid(row=5, column=0, sticky="ew", padx=18, pady=(0, 14))

    def _append_log(self, message: str) -> None:
        self.log_queue.put(message)

    def _drain_logs(self) -> None:
        flushed = False
        while True:
            try:
                item = self.log_queue.get_nowait()
            except queue.Empty:
                break
            flushed = True
            self.log_text.configure(state="normal")
            self.log_text.insert("end", item + "\n")
            self.log_text.see("end")
            self.log_text.configure(state="disabled")

        if flushed:
            self.update_idletasks()
        self.after(120, self._drain_logs)

    def set_status(self, message: str) -> None:
        self.status_var.set(message)

    def refresh_plugins(self) -> None:
        previous_states = {key: var.get() for key, var in self.plugin_vars.items()}
        self.plugins = discover_plugin_dlls()
        self.plugin_vars = {}
        self.plugins_label.configure(text=f"Available Plugins: {len(self.plugins)}")

        for widget in self.plugins_scroll.winfo_children():
            widget.destroy()

        if not self.plugins:
            empty_label = ctk.CTkLabel(
                self.plugins_scroll,
                text="No plugin DLLs were found under a releases folder.",
                anchor="w",
            )
            empty_label.grid(row=0, column=0, sticky="w", padx=4, pady=6)
            return

        for row, dll in enumerate(self.plugins):
            key = dll.name.lower()
            default_checked = previous_states.get(key, True)
            var = ctk.BooleanVar(value=default_checked)
            self.plugin_vars[key] = var

            label = f"{plugin_display_name(dll)}  ({dll.parent.name})"
            checkbox = ctk.CTkCheckBox(self.plugins_scroll, text=label, variable=var)
            checkbox.grid(row=row, column=0, sticky="w", padx=4, pady=4)

    def select_all_plugins(self) -> None:
        for var in self.plugin_vars.values():
            var.set(True)

    def clear_all_plugins(self) -> None:
        for var in self.plugin_vars.values():
            var.set(False)

    def get_selected_plugins(self) -> list[Path]:
        selected: list[Path] = []
        for dll in self.plugins:
            key = dll.name.lower()
            var = self.plugin_vars.get(key)
            if var is not None and var.get():
                selected.append(dll)
        return selected

    def browse_target(self) -> None:
        selected = filedialog.askdirectory(title="Select SCrawler Plugins Folder")
        if selected:
            self.target_var.set(selected)

    def start_auto_detect(self) -> None:
        if self.worker_lock.locked():
            return

        thread = threading.Thread(target=self._detect_worker, daemon=True)
        thread.start()

    def _detect_worker(self) -> None:
        with self.worker_lock:
            self.after(0, lambda: self.set_status("Detecting SCrawler location..."))
            self._append_log("Searching for SCrawler install folder...")

            detected = detect_scrawler_plugins_dir()
            if detected:
                self.after(0, lambda: self.target_var.set(str(detected)))
                self._append_log(f"Detected plugins folder: {detected}")
                self.after(0, lambda: self.set_status("SCrawler location detected."))
            else:
                self._append_log("Auto-detect did not find SCrawler. Pick the folder manually.")
                self.after(0, lambda: self.set_status("Auto-detect failed."))

    def install_all(self) -> None:
        if self.worker_lock.locked():
            return

        if not self.plugins:
            messagebox.showerror(APP_TITLE, "No plugin DLLs were found to install.")
            return

        selected_plugins = self.get_selected_plugins()
        if not selected_plugins:
            messagebox.showerror(APP_TITLE, "Select at least one plugin to install.")
            return

        raw_target = self.target_var.get()
        target = normalize_target_plugins_dir(raw_target)
        if target is None:
            messagebox.showerror(APP_TITLE, "Select the SCrawler Plugins folder first.")
            return

        thread = threading.Thread(target=self._install_worker, args=(target, selected_plugins), daemon=True)
        thread.start()

    def _install_worker(self, target: Path, selected_plugins: list[Path]) -> None:
        with self.worker_lock:
            self.after(0, lambda: self.install_btn.configure(state="disabled"))
            self.after(0, lambda: self.set_status("Installing plugins..."))

            try:
                target.mkdir(parents=True, exist_ok=True)
                self._append_log(f"Installing to: {target}")

                copied = 0
                for dll in selected_plugins:
                    destination = target / dll.name
                    shutil.copy2(dll, destination)
                    copied += 1
                    self._append_log(f"Installed: {dll.name}")

                self.after(0, lambda: self.set_status(f"Installed {copied} plugin(s)."))
                self._append_log(f"Completed. Installed {copied} plugin(s).")
                self.after(0, lambda: messagebox.showinfo(APP_TITLE, f"Installed {copied} plugin(s) to:\n{target}"))
            except Exception as ex:
                self._append_log(f"Install failed: {ex}")
                self.after(0, lambda: self.set_status("Install failed."))
                self.after(0, lambda: messagebox.showerror(APP_TITLE, f"Installation failed:\n{ex}"))
            finally:
                self.after(0, lambda: self.install_btn.configure(state="normal"))


def main() -> None:
    app = InstallerApp()
    app.mainloop()


if __name__ == "__main__":
    main()
