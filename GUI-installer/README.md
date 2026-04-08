# GUI-installer

Lightweight `customtkinter` installer for SCrawler plugins.

## What it does

- Auto-detects SCrawler install location on launch (including Desktop/Documents and OneDrive Desktop/Documents paths).
- Auto-fills the `Plugins` destination folder.
- Lists plugins as checkbox items so users can install selected plugins only.
- Installs selected plugin DLLs from `..\releases\*\SCrawler.Plugin.*.dll`.

## Run from source

```powershell
python -m pip install -r .\requirements.txt
python .\installer.py
```

## Build one-file EXE

```powershell
powershell -ExecutionPolicy Bypass -File .\build-exe.ps1 -Clean
```

Output:

- `.\dist\SCrawler-Plugin-Installer.exe`
