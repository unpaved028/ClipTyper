# ClipTyper

> **The last mile for text that cannot be pasted.**  
> ClipTyper types clipboard text — passwords, commands, scripts, and config blocks — into Remote Desktops, server KVMs, and locked-down fields, character by character via Windows `SendInput`.

[![Release](https://img.shields.io/github/v/release/unpaved028/ClipTyper?label=download&color=blue)](https://github.com/unpaved028/ClipTyper/releases/latest)
[![Tests](https://img.shields.io/badge/tests-24%2F24%20passing-brightgreen)](ClipTyper.Tests)
[![License](https://img.shields.io/github/license/unpaved028/ClipTyper)](LICENSE)
[![WinGet](https://img.shields.io/badge/winget-unpaved028.ClipTyper-blue)](https://github.com/microsoft/winget-pkgs/tree/master/manifests/u/unpaved028/ClipTyper)

## Why ClipTyper?

When clipboard paste (`Ctrl+V`) is blocked or unavailable, manual retyping is slow, painful, and prone to typos. ClipTyper bridges this gap without requiring network access, server-side agents, or administrator rights.

**Common scenarios where ClipTyper helps:**
- 🖥️ **Remote Desktop & VDI:** Fullscreen RDP, Citrix Workspace, or VMware Horizon where clipboard redirection is disabled by GPO.
- 🖲️ **Out-of-Band Management & KVM:** HP iLO, Dell iDRAC, Supermicro IPMI, and Raritan KVM-over-IP HTML5 consoles with no clipboard channel.
- ☁️ **Cloud & Hypervisor Consoles:** AWS EC2 Serial Console, Azure Serial Console, Proxmox noVNC/SPICE, and VMware ESXi / vSphere web consoles.
- 🤝 **Remote Support & Screen Sharing:** Microsoft Teams screen control sharing or Zoom remote sessions where the viewer's clipboard is not bridged.
- 🔒 **Security & Banking Portals:** Web forms and password fields that intentionally disable paste events (`onpaste="return false;"`).

## 🎯 Target Compatibility Matrix

Tested configurations and recommended settings for common remote and restricted environments:

| Target Environment | Why Paste Fails | Recommended Settings | Practical Tip |
|---|---|---|---|
| **Remote Desktop (RDP / mstsc) — Windowed** | Clipboard redirection disabled by GPO | Delay: `25 ms`<br>Mode: `Unicode`<br>Trigger: `Ctrl + Shift + T` | Default configuration works out of the box. |
| **Remote Desktop (RDP / mstsc) — Fullscreen** | Global hotkeys forwarded to remote OS | Delay: `25 ms`<br>Mode: `Unicode`<br>**Trigger: Floating Overlay** | Use screen-edge Overlay button (or toggle visibility with `Ctrl + Shift + H`). |
| **Citrix Workspace / VMware Horizon** | Policy cuts clipboard bridging | Delay: `35–50 ms`<br>Mode: `Unicode` or `VK Mode` | Increase delay if high network latency causes dropped characters. |
| **HP iLO / Dell iDRAC (HTML5)** | Canvas console accepts raw keystrokes only | Delay: `40–60 ms`<br>Mode: `VK Compatibility Mode` | VK mode translates to physical keycodes required by canvas emulators. |
| **Proxmox noVNC / ESXi Web Console** | Web terminal has no clipboard pipe | Delay: `30–45 ms`<br>Mode: `Unicode`<br>Newline: `Enter` | Plain Unicode typing passes cleanly to browser websocket terminal. |
| **AWS EC2 / Azure Serial Console** | Raw TTY/COM serial stream | Delay: `30 ms`<br>Plain-Text: `Enabled` | Plain-text enforcement avoids terminal escape sequence corruption. |
| **MS Teams Screen Control / Zoom** | Viewer clipboard not bridged across WebRTC | Delay: `30–50 ms`<br>Mode: `VK Compatibility Mode`<br>Newline: `Shift + Enter` (chat) | VK mode bypasses WebRTC synthetic keystroke capture limitations. |
| **Paste-Blocked Password Fields** | JavaScript intercepts paste events | Delay: `15–25 ms`<br>Mode: `Credential Auto-Type` | Auto-detects Tab/Newline delimiters; optional auto-clear clipboard. |

> 💡 **Tested another environment?** Submit a [Target Compatibility Report](https://github.com/unpaved028/ClipTyper/issues/new?template=target_compatibility.md) to help expand this matrix!

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
| **Slim** | Single `.exe` (~1.5 MB), requires installed .NET 8 Runtime | ✅ [Install Runtime](https://dotnet.microsoft.com/download/dotnet/8.0/runtime) | **[ClipTyper-Slim.exe](https://github.com/unpaved028/ClipTyper/releases/latest/download/ClipTyper-Slim.exe)** |

> 💡 **Not sure which version to choose?** Grab the **Installer** — it sets up everything automatically without requiring any Administrator privileges.

## Features

- 🎯 **Paste anywhere** — works in RDP sessions, KVM consoles, web terminals, and password fields that block clipboard paste
- 🔑 **Credential Auto-Type** — two-stage typing for username and password with delimiter detection (Tab / Newline / Custom) and transition keys
- ↩️ **Flexible Newline Handling** — customizable behavior for newlines (`Enter`, `Shift + Enter` for chat apps, replace with space, or ignore)
- 📄 **Plain-Text Mode** — prefer Unicode plain-text clipboard format over RTF/HTML formatting
- 🎲 **Humanized Typing Jitter** — optional random typing variance (1–50ms, default ±5ms) to simulate natural keystrokes
- 📦 **Fully portable** — single `.exe`, no installation needed
- 🔒 **No admin rights** — runs entirely in user-space
- ⌨️ **Hardware-level input** — uses `SendInput` with Unicode characters or optional VK keycode mapping
- 🛡️ **Typing safety & robustness** — Escape emergency abort, focus-loss protection, single-instance guard, max text length confirmation dialog, and input sanitization (BOM, null-bytes, zero-width spaces filter)
- ⚡ **Teams & Remote Desktop compatibility** — optional VK compatibility mode translating characters to real virtual keycodes for MS Teams control share, Citrix, and RDP
- 🛡️ **UIPI elevation warning** — clear alert dialog when attempting to type into administrator-elevated target windows
- 🕐 **Configurable timing** — adjustable keystroke delay (5–100ms) ensures no characters are dropped
- 🖥️ **Silent background app** — runs as a system tray icon with single-instance guard
- 🖱️ **Floating overlay button** — optional on-screen button with multi-monitor support, edge snapping, live-preview scaling (25% - 200%), pulsing typing animation, completion checkmark, and customizable visibility toggle hotkey
- ⚙️ **Customizable hotkeys** — change the trigger hotkey and the overlay visibility toggle hotkey to any combinations you prefer
- 🔄 **Automated update check** — checks for new releases on startup (24h throttled) with native tray notifications, visual update badges on tray icon and overlay, and release notes display in the About dialog

## Usage

### Basic (Hotkey)

1. **Start** `ClipTyper.exe` — it appears in the system tray (notification area)
2. **Copy** any text to your clipboard (`Ctrl+C`)
3. **Click** into the target window where you want to type
4. **Press** `Ctrl + Shift + T` — ClipTyper types the clipboard content character by character

> **Tip:** Press `Escape` at any time during typing to abort instantly!

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
| **Newline Handling** | How newlines are sent: `Enter`, `Shift + Enter` (Chats), `Space`, or `Ignore`. | `Enter (Default)` |
| **Enforce Plain-Text** | Prefers Unicode plain-text clipboard format over RTF/HTML formatting. | Disabled |
| **Typing Jitter** | Adds random variance (1–50ms, default ±5ms) to simulate natural typing. | Disabled |
| **Credential Auto-Type** | Two-stage login: Auto-detects delimiters (Tab/Newline/Custom) to type user and password. | Disabled |
| **Custom Transition Key** | Key sent between username and password in Custom mode (`Tab` or `Enter`). | `Tab` |
| **Stage Pause** | Delay (50–2000ms) between username and password during credential auto-typing. | 200ms |
| **Auto-Clear Clipboard** | Automatically erases clipboard contents a few seconds after typing credentials. | Disabled |
| **Clean Text** | Removes invisible control chars, BOM, null-bytes, zero-width spaces before typing. | Enabled |
| **Confirm Before Long Text** | Shows duration estimate confirmation dialog before typing long clipboard text. | Enabled (> 5,000 chars) |
| **VK Compatibility Mode** | Translates characters to physical virtual keycodes for MS Teams control share / RDP. | Disabled |
| **Play Sound Signal** | Plays a system sound signal upon typing completion. | Disabled |
| **Diagnostic Logging** | Logs non-sensitive diagnostic events to local `clip-typer.log`. | Enabled |
| **Show Overlay Button** | Enable/disable the floating overlay button. | Disabled |
| **Scale** | Scale percentage slider (25%–200%) with real-time preview. | 100% |
| **Monitor** | Select which display monitor to position the overlay on. | Primary Monitor |
| **Overlay Toggle Hotkey** | Keyboard shortcut to show/hide the floating overlay button (can be disabled). | `Ctrl + Shift + H` |
| **Reset Position** | Reset the overlay to the default position (right edge, center). | — |
| **Run at Startup** | *(Installed version only)* Run ClipTyper automatically on Windows boot. | Enabled |
| **Automatically check for updates** | Check GitHub automatically on launch for new releases (max 1x per 24h). | Enabled |

## How It Works

```
Clipboard text → SendInput (Unicode keystrokes) → Target application
```

- High-frequency simulation loop translating string content into hardware input events via `SendInput`.
- Safe threading: typing is executed on a dedicated background worker, keeping the UI responsive.
- Single-instance concurrency guard ensures overlapping typing triggers are safely ignored.
- No administrator privileges required for installation or autostart (uses HKCU).

## Building from Source

```bash
# Clone
git clone https://github.com/unpaved028/ClipTyper.git
cd ClipTyper

# Build and run tests
dotnet test ClipTyper.sln -c Release

# 1. Portable (self-contained, compressed, with portable.marker)
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o ./publish-portable
New-Item -Path ./publish-portable/portable.marker -ItemType File

# 2. Slim (framework-dependent, with portable.marker)
dotnet publish -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true -o ./publish-slim
New-Item -Path ./publish-slim/portable.marker -ItemType File

# 3. WinGet (installed mode, no marker, settings in AppData)
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o ./publish-winget

# 4. Compile Installer (requires Inno Setup installed)
iscc /DMyAppVersion=1.6.1 setup.iss
```

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) for building.

## Changelog

### v1.6.1
- 🛑 **Stop Typing Anytime**: Cancel active typing instantly from the system tray menu, the floating overlay menu, or by clicking the active overlay.
- 🎯 **Target Compatibility Matrix**: Comprehensive guide and presets for RDP, Citrix, VMware Horizon, iLO/iDRAC, and web serial consoles.
- 🛡️ **Threat Model & Security Policy**: Documented security boundaries, UIPI protection, and zero-telemetry privacy commitment in `SECURITY.md`.
- 📁 **Smart Read-Only Fallback**: Automatically detects write-protected media (USB/network shares) in portable mode and safely redirects settings to LocalAppData.
- 🔒 **Diagnostic Logging Control**: Easily enable or disable diagnostic logging (`clip-typer.log`) directly in Settings.
- 🔔 **Actionable Alerts**: Clear notification balloons for startup hotkey conflicts, read-only locations, or empty clipboards.
- 🎯 **Custom Delimiter Transition Key**: Choose whether ClipTyper presses `Tab` or `Enter` after your custom credential delimiter.
- 🔐 **Release Integrity**: Automated `SHA256SUMS.txt` cryptographic checksums attached to every release.
- ⚙️ **Under-the-Hood Hardening**: Cleaned thread synchronization, isolated hermetic test suite (24/24 passing), and zero compiler warnings under latest code analysis.

### v1.6.0
- 🔑 **Credential Auto-Type**: Automatic two-stage typing for username and password. Detects Tab, Newline, or custom delimiters in copied text, types the username, presses Tab/Enter, pauses briefly, and safely types the password.
- 🔒 **Credential Security**: Passwords are never logged. Typing stops immediately if the target window loses focus before typing the password. Optional automatic clipboard clearing after login.
- ↩️ **Configurable Newlines**: Choose how line breaks are processed (`Enter`, `Shift + Enter` for MS Teams & chat apps, replace with space, or ignore).
- 📄 **Plain-Text Mode**: Strips Rich Text and HTML formatting from copied text before typing.
- 🎲 **Humanized Typing Jitter**: Adds subtle random variance to typing intervals to simulate natural keyboard input.
- 🛡️ **Core Refactoring & Stability**: Isolated background `TypingService` preventing parallel typing collision, thread-safe UI prompts, and memory leak fixes.

### v1.5.1
- 🛠️ **Installer App Shutdown Fix**: Fixed issue where the Inno Setup installer hung at "Closing Applications..." when ClipTyper was running. Added graceful Windows message shutdown handling (`WM_CLOSE`, `WM_QUERYENDSESSION`, `WM_ENDSESSION`), Inno Setup `AppMutex` registration, and a process termination failsafe during installation.

### v1.5.0
- 🛡️ **Emergency Abort & Focus Safety**: Immediate cancellation of typing when pressing `Escape` or when focus switches to another window.
- 🔒 **Single-Instance Guard**: Mutex-based check prevents launching multiple ClipTyper instances simultaneously.
- ⚡ **Teams & Remote Desktop Compatibility**: New optional VK compatibility mode for Microsoft Teams control share, RDP, and VMs.
- 📝 **Input Sanitization**: Automatic cleaning of clipboard text before typing (filters BOM, null-bytes, and invisible control characters).
- ⚠️ **Long Text Protection**: Configurable confirmation dialog before typing very long texts with estimated duration.
- 🛡️ **Admin Rights Detection (UIPI)**: Clear alert dialog when target window is running with Administrator privileges.
- 📊 **Visual Progress & Logging**: Progress animation on overlay and tray icon, plus structured rotating file logging (`clip-typer.log`).

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
