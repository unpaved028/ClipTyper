# ClipTyper

A lightweight, portable Windows tool that types clipboard text as simulated keyboard input — character by character.

**Why?** Some environments block `Ctrl+V` paste (Remote Desktops, KVM consoles, web-based terminals, restricted VMs). ClipTyper bypasses this by simulating real keystrokes via the Windows `SendInput` API.

## ⬇️ Download & Installation

### Windows Package Manager (WinGet)
You can install ClipTyper directly from your terminal:
```cmd
winget install unpaved028.ClipTyper
```

### Manual Download
| Variant | Description | .NET Runtime Required? | Download |
|---|---|---|---|
| **Installer (Recommended)** | Standard non-admin Setup.exe – automatically creates Start Menu shortcuts and handles Autostart | ❌ No | **[ClipTyper-Setup.exe](https://github.com/unpaved028/ClipTyper/releases/latest/download/ClipTyper-Setup.exe)** |
| **Portable** | Single `.exe` self-contained with .NET Runtime – runs instantly anywhere | ❌ No | **[ClipTyper-Portable.exe](https://github.com/unpaved028/ClipTyper/releases/latest/download/ClipTyper-Portable.exe)** |
| **Slim** | Single `.exe` (~1 MB), requires installed .NET 8 Runtime | ✅ [Install Runtime](https://dotnet.microsoft.com/download/dotnet/8.0/runtime) | **[ClipTyper-Slim.exe](https://github.com/unpaved028/ClipTyper/releases/latest/download/ClipTyper-Slim.exe)** |

> 💡 **Not sure which version to choose?** Grab the **Installer** — it sets up everything automatically without requiring any Administrator privileges.

## Features

- 🎯 **Paste anywhere** — works in RDP sessions, KVM consoles, web terminals, and password fields that block clipboard paste
- 📦 **Fully portable** — single `.exe`, no installation needed
- 🔒 **No admin rights** — runs entirely in user-space
- ⌨️ **Hardware-level input** — uses `SendInput` with Unicode characters, not `SendKeys`
- 🕐 **Configurable timing** — adjustable keystroke delay (5–100ms) ensures no characters are dropped
- 🖥️ **Silent background app** — runs as a system tray icon, no window
- 🖱️ **Floating overlay button** — optional on-screen button with multi-monitor support, edge snapping, live-preview scaling (25% - 200%), and a customizable visibility toggle hotkey
- ⚙️ **Customizable hotkeys** — change the trigger hotkey and the overlay visibility toggle hotkey to any combinations you prefer
- 🔄 **Update checker** — check for new versions directly from the About dialog

## Usage

### Basic (Hotkey)

1. **Start** `ClipTyper.exe` — it appears in the system tray (notification area)
2. **Copy** any text to your clipboard (`Ctrl+C`)
3. **Click** into the target window where you want to type
4. **Press** `Ctrl + Shift + T` — ClipTyper types the clipboard content character by character

> **Tip:** For passwords, copy the password first, then click into the password field and press `Ctrl+Shift+T`.

### Overlay Button (for RDP / Fullscreen Sessions)

When using fullscreen Remote Desktop, global hotkeys like `Ctrl+Shift+T` are forwarded to the remote session. The overlay button solves this:

1. **Enable** the overlay: Right-click tray icon → **Settings** → check **"Show Overlay Button"** → Save. (You can also toggle its visibility at any time using the `Ctrl+Shift+H` hotkey).
2. A small, semi-transparent ClipTyper icon appears at the edge of your screen
3. **Hover** over it — it slides out from the edge
4. **Left-click** it — ClipTyper automatically restores focus to your previous window and types the clipboard content
5. **Right-click** it to hide, select the active monitor, or select a scale preset (50%/100%/200%)

The overlay can be **dragged** anywhere on screen. When dragged near a screen edge (Left, Right, Top, Bottom) on *any* monitor, it snaps and peeks out cleanly.

## Settings

Right-click the tray icon → **Settings** to configure:

| Setting | Description | Default |
|---|---|---|
| **Trigger Hotkey** | The keyboard shortcut to trigger typing. Validates if the hotkey is in use. | `Ctrl + Shift + T` |
| **Keystroke Delay** | Delay between each simulated keystroke (5–100ms). Increase for slow/remote targets. | 25ms |
| **Show Overlay Button** | Enable/disable the floating overlay button. | Disabled |
| **Scale** | Scale percentage slider (25%–200%) with real-time preview. | 100% |
| **Monitor** | Select which display monitor to position the overlay on. | Primary Monitor |
| **Overlay Toggle Hotkey** | Keyboard shortcut to show/hide the floating overlay button. | `Ctrl + Shift + H` |
| **Reset Position** | Reset the overlay to the default position (right edge, center). | — |
| **Run at Startup** | *(Installed version only)* Run ClipTyper automatically on Windows boot. | Enabled |

**Settings Storage:**
- **Installed Version (Installer/WinGet):** Program is installed under `%LocalAppData%\Programs\ClipTyper`. Settings are saved to `%AppData%\ClipTyper\settings.json`.
- **Portable / Slim Versions:** Settings are saved as `settings.json` in the same folder as the executable (enabled by the `portable.marker` file).

## How It Works

```
Clipboard text → SendInput (Unicode keystrokes) → Target application
```

ClipTyper reads text from the Windows clipboard and uses the native `SendInput` API with `KEYEVENTF_UNICODE` to simulate individual key presses. Each character is sent as a separate KeyDown/KeyUp pair with a configurable delay between characters, ensuring reliable input even in slow or remote environments.

Before typing begins, all modifier keys (Ctrl, Shift, Alt) are programmatically released to prevent the hotkey combination from interfering with the typed text.

### Overlay Focus Restoration

When triggered via the overlay button, ClipTyper:
1. Continuously tracks the last active window (that isn't ClipTyper)
2. Waits 500ms after the click
3. Restores focus to that window using `SetForegroundWindow`
4. Waits another 500ms for the target to process the focus change
5. Reads the clipboard and begins typing

## System Requirements

- Windows 10/11 (x64)
- **Portable Version:** No additional requirements
- **Slim Version:** [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0/runtime) required
- No administrator privileges required for installation or autostart (uses HKCU).

## Building from Source

```bash
# Clone
git clone https://github.com/unpaved028/ClipTyper.git
cd ClipTyper

# 1. Portable (self-contained, trimmed, compressed, with portable.marker)
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o ./publish-portable
New-Item -Path ./publish-portable/portable.marker -ItemType File

# 2. Slim (framework-dependent, with portable.marker)
dotnet publish -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true -o ./publish-slim
New-Item -Path ./publish-slim/portable.marker -ItemType File

# 3. WinGet (installed mode, no marker, settings in AppData)
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o ./publish-winget

# 4. Compile Installer (requires Inno Setup installed)
iscc /DMyAppVersion=1.4.0 setup.iss
```

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) for building.

## Changelog

### v1.4.0
- **New Inno Setup Installer**: Introduced a user-space non-admin installer (`ClipTyper-Setup.exe`) that automatically configures Start Menu shortcuts and Autostart registry settings during installation, and launches the app automatically.
- **Overlay Scaling**: Replaced size presets with a continuous Scale percentage slider (25% - 200%) in the settings form, including a real-time live preview.
- **Multi-Monitor Positioning**: Added support for choosing which display monitor the overlay button resides on, and ensured relative coordinates are preserved on monitor switch.
- **All-Edge Peeking & Snapping**: The overlay button can now snap and peek vertically (top/bottom) and horizontally (left/right) on any display.
- **Adjacent Monitor Bleeding Fix**: Implemented boundary region clipping to prevent the floating icon from bleeding onto adjacent screens when parked.
- **Visibility Toggle Shortcut**: Added a global hotkey (default `Ctrl + Shift + H`, fully customizable) to easily toggle the overlay button on/off.

### v1.2.0
- Added manual "Check for Updates" feature in the About dialog linking to Github Release API.
- Implemented settings validation to prevent binding system-reserved or blocked hotkeys.

## License

MIT
