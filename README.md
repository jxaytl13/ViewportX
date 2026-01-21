# ViewPortX (Unity Editor Tool) – Offline Documentation

## 1. Table of Contents

1. Overview  
2. Requirements  
3. Installation  
4. Quick Start  
5. Controls & UI Reference  
6. Settings & Data Storage  
7. File Layout (UXML/USS & Icons)  
8. Troubleshooting  
9. Contact / Support  

## 2. Overview

ViewPortX is a Unity Editor window for quickly previewing selected assets in a dedicated viewport (models, prefabs, and particles), with common navigation controls such as orbit, pan, zoom, framing, grid, lighting, and projection toggle.

No runtime components are required. This tool is intended for the Unity Editor only.

## 3. Requirements

- Unity 2021.3 LTS or newer (Editor tool)
- Works in the Editor (Mono). No IL2CPP/runtime player integration is required.

## 4. Installation

1. Import the package into your Unity project.
2. Ensure the folder structure is preserved (see “File Layout” below).
3. Open the window via the menu:
   - `Window/T·L Nexus/ViewPortX`

## 5. Quick Start

1. Open `ViewPortX` from the menu.
2. Select a prefab/model/particle asset in the Project window.
3. The window updates the preview automatically (you can also use the refresh button if available).
4. Use the toolbar buttons to toggle grid, lighting, projection, auto-rotate, and to frame/reset the view.

## 6. Controls & UI Reference

The exact controls can vary slightly by Unity version and platform, but the window generally provides:

1. Selection info (current selection name/status)
2. Preview area (renders the selected content)
3. Toolbar buttons for:
   - Play/Pause particles
   - Restart particles
   - Grid visibility
   - Auto rotate
   - Lighting on/off
   - Perspective/Orthographic toggle
   - Refresh selection
   - Reset view
   - View axis shortcuts (X/Y/Z)
   - Frame view / focus
   - Settings

## 7. Settings & Data Storage

ViewPortX stores user preferences under the project `Library` folder (per-project):

- Folder: `Library/ViewPortX`
- File: `ViewPortXConfig.json`

Deleting the file resets settings to defaults.

## 8. File Layout (UXML/USS & Icons)

ViewPortX uses UI Toolkit assets (UXML/USS) and toolbar icon textures.

To avoid missing UI at runtime, keep these files (and their `.meta` files) together as shipped:

- `UI/ViewPortXWindow.uxml`
- `UI/ViewPortXWindow.uss`
- Icon textures referenced by the window (located next to the UXML/USS in the UI directory)

If you move or rename UI files, Unity GUIDs will change and the window may show “Missing UXML/USS”.

## 9. Troubleshooting

1. The window opens but is blank  
   - Check the Console for errors.
   - Ensure `UI/ViewPortXWindow.uxml` and `UI/ViewPortXWindow.uss` exist and their `.meta` files were not regenerated.
   - If you are using an obfuscated DLL, make sure the UI build is not dependent on a renamed `CreateGUI()` method (this package avoids relying on `CreateGUI()` as the only entry point).

2. “Missing UXML/USS” message inside the window  
   - The UI assets are missing or their GUID/path mapping changed. Restore the original UI files and `.meta`.

3. Icons are missing in the toolbar  
   - Ensure the icon textures are present in the same directory as the UXML file.

## 10. Contact / Support

If you need help, include the following in your report:

1. Unity version (e.g. 2021.3.x)
2. OS (Windows/macOS)
3. Console error stack trace (if any)
4. A screenshot of the ViewPortX window

