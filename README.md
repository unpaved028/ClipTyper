# ClipTyper

**The last mile for text that cannot be pasted.**

Type clipboard text as simulated keystrokes to bypass paste restrictions in VM consoles (vSphere, Proxmox, Hyper-V), iDRAC/iLO, RDP sessions, and paste-blocked fields.

[![Latest Release](https://img.shields.io/github/v/release/unpaved028/ClipTyper?label=latest%20release&color=0B5FFF)](https://github.com/unpaved028/ClipTyper/releases/latest)
[![WinGet](https://img.shields.io/badge/winget-unpaved028.ClipTyper-0B5FFF)](https://github.com/microsoft/winget-pkgs/tree/master/manifests/u/unpaved028/ClipTyper)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-111827)](https://github.com/unpaved028/ClipTyper/releases/latest)
[![License: MIT](https://img.shields.io/github/license/unpaved028/ClipTyper)](LICENSE)
[![CI](https://github.com/unpaved028/ClipTyper/actions/workflows/ci.yml/badge.svg)](https://github.com/unpaved028/ClipTyper/actions/workflows/ci.yml)

```powershell
winget install unpaved028.ClipTyper
```

Or grab **[ClipTyper-Setup.exe](https://github.com/unpaved028/ClipTyper/releases/latest/download/ClipTyper-Setup.exe)** · **[Portable](https://github.com/unpaved028/ClipTyper/releases/latest/download/ClipTyper-Portable.exe)** · **[All releases](https://github.com/unpaved028/ClipTyper/releases/latest)**

> **Official downloads only from GitHub Releases or Winget.** Third-party mirrors (including Softonic) may ship outdated or wrapped builds.

<p align="center">
  <img src="media/cliptyper-demo.gif" alt="In a remote console Ctrl+V does nothing; after Ctrl+Alt+V, ClipTyper types the clipboard as key presses" width="920" />
</p>

<p align="center"><sub>Demo console that only accepts key presses, like iLO, iDRAC or noVNC: Ctrl+V does nothing, Ctrl+Alt+V types the clipboard.</sub></p>

---

## Why ClipTyper?

When `Ctrl+V` is blocked or unavailable, retyping passwords and commands by hand is slow and error-prone. ClipTyper reads the Windows clipboard on your trigger and types the text character by character via native `SendInput` — no agents on the remote side, no admin rights required for normal use.

A focused alternative to **ClickPaste**, AutoHotkey paste scripts, or on-screen virtual keyboards — with console keyboard layouts (type into a US iLO from a German PC), a fullscreen RDP overlay, credential auto-type, Winget installs, and an explicit [zero-telemetry](SECURITY.md) policy.

---

## Target scenarios / tested with

| Environment | Why paste fails | What to use |
|---|---|---|
| **VMware vSphere / VMRC** | Console has no (or blocked) clipboard channel | Delay 30–45 ms · Unicode or VK mode |
| **Proxmox VE noVNC / SPICE** | Browser console ≠ OS clipboard | Delay 30–45 ms · Unicode · Newline: Enter |
| **Hyper-V VM Connection** | Limited clipboard bridging | Delay 25–40 ms · Unicode |
| **Dell iDRAC / HPE iLO / IPMI HTML5** | Canvas console accepts keystrokes only | Delay 40–60 ms · **Key presses (VK)** · Keyboard layout = server layout |
| **KVM-over-IP** (Raritan and similar) | No clipboard pipe into the session | Overlay or hotkey · slower delay if laggy |
| **RDP / Citrix / Horizon** (clipboard GPO-blocked) | Redirection disabled by policy | Hotkey in windowed mode · **Overlay** in fullscreen |
| **AWS EC2 / Azure Serial Console** | Raw TTY stream | Delay ~30 ms |
| **MS Teams / Zoom remote control** | Viewer clipboard not bridged | **VK Compatibility Mode** · Newline: Shift+Enter for chat |
| **Paste-blocked web/password fields** | `onpaste` / JS blocks clipboard | Credential Auto-Type · optional clipboard auto-clear |

> Tested another console? Open a [Target Compatibility Report](https://github.com/unpaved028/ClipTyper/issues/new?template=target_compatibility.md).

<p align="center">
  <img src="media/github-social-preview.png" alt="ClipTyper overlay on an HPE iLO remote console login — clipboard typed as keystrokes where paste is blocked" width="720" />
</p>

<p align="center"><sub>HPE iLO remote console: paste is blocked, ClipTyper types the clipboard as keystrokes (overlay on the right).</sub></p>

### Quick settings cheat sheet

| Target | Recommended settings |
|---|---|
| RDP windowed | Delay `25 ms` · Unicode · `Ctrl + Alt + V` |
| RDP fullscreen | Delay `25 ms` · Unicode · **Floating Overlay** (`Ctrl + Shift + H` to toggle) |
| Citrix / Horizon | Delay `35–50 ms` · Unicode or VK |
| iLO / iDRAC HTML5 | Delay `40–60 ms` · **Key presses (VK)** · Keyboard layout = server layout |

---

## Download & installation

### Winget (recommended)

```powershell
winget install unpaved028.ClipTyper
```

### Scoop

```powershell
scoop bucket add unpaved028 https://github.com/unpaved028/scoop-bucket
scoop install unpaved028/cliptyper
```

Installs the Portable build; settings are kept across `scoop update`.

### Manual download

| Variant | Description | .NET Runtime? | Download |
|---|---|---|---|
| **Installer** | Non-admin Setup.exe — Start Menu + Autostart | No | **[ClipTyper-Setup.exe](https://github.com/unpaved028/ClipTyper/releases/latest/download/ClipTyper-Setup.exe)** |
| **Portable** | Self-contained single `.exe` | No | **[ClipTyper-Portable.exe](https://github.com/unpaved028/ClipTyper/releases/latest/download/ClipTyper-Portable.exe)** |
| **Slim** | Under 1 MB; needs .NET 10 Desktop Runtime | [Yes](https://dotnet.microsoft.com/download/dotnet/10.0) | **[ClipTyper-Slim.exe](https://github.com/unpaved028/ClipTyper/releases/latest/download/ClipTyper-Slim.exe)** |

Not sure? Use the **Installer**.

Integrity:
- Checksums: [`SHA256SUMS.txt`](https://github.com/unpaved028/ClipTyper/releases/latest) on every release
- **ClipTyper-Setup.exe (v1.8.0)** SHA-256: `d115c50fbdbea9e48f90871cf70c3650bb146c10521d086335d770b3b2ef48fc`
- VirusTotal: [**0 detections**](https://www.virustotal.com/gui/file/bbaa17b32de4b381edbe98277b0a0a342331947b2e0f96c47d19276545a25c4c) on the v1.7.0 installer (rescan 7 October 2026). 68 engines reported no detection; 2 returned no result. Microsoft does not flag this file. Unsigned `SendInput` tools can still trip heuristics — see [SECURITY.md](SECURITY.md).

---

## Trust & privacy

- **Zero telemetry** — no analytics and no product metrics. See [SECURITY.md](SECURITY.md).
- **Clipboard stays local** — read only when you trigger typing; never written to disk or sent to a server.
- **Almost offline** — the only network call is an optional GitHub Releases update check. It is on by default and can be turned off in Settings. No other outbound traffic.
- **Open source (MIT)** — review the code; verify release checksums.
- **UIPI respected** — non-elevated ClipTyper will not silently type into Administrator windows.

---

## Features

- Paste anywhere paste is blocked — RDP, KVM, web consoles, locked password fields
- Two typing methods: **characters (Unicode)** for local apps and RDP, or **key presses (VK Compatibility Mode)** for consoles, with a selectable **console keyboard layout** (e.g. type into a US-layout iLO console from a German PC)
- Switch typing method and console layout right from the tray or overlay menu
- **Credential Auto-Type** — two-stage username/password with Tab / Newline / custom delimiters
- Flexible newlines — `Enter`, `Shift + Enter`, `Space`, or `Ignore`
- Always types the plain-text clipboard content (RTF/HTML formatting is ignored)
- Optional humanized typing jitter (±1–50 ms)
- Escape abort, focus-loss protection, single-instance guard, long-text confirmation, input sanitization
- Floating **overlay** for fullscreen RDP (edge snap, multi-monitor, size 25–200%)
- Configurable hotkeys (type + overlay toggle)
- Light and dark mode (follows Windows, or choose in Settings), Windows 11 style menus, crisp icons on scaled displays
- Portable single-exe or non-admin installer
- Optional auto update check with tray/overlay badge

---

## Usage

### Hotkey

1. Start ClipTyper — tray icon appears  
2. Copy text (`Ctrl+C`)  
3. Focus the target window  
4. Press **`Ctrl + Alt + V`** — ClipTyper types the clipboard  

Press **`Escape`** (or tray **Stop Typing**) to abort. Double-click the tray icon to open Settings.

### Remote console with a different keyboard layout

Consoles such as iLO, iDRAC, vSphere/VMRC, or KVM-over-IP pass on key presses, and the server interprets them with its own keyboard layout. If your PC uses German and the server uses US, special characters in passwords come out wrong.

In **Settings → Keyboard**, choose **Key presses (VK Compatibility Mode)** and set the **keyboard layout** to the server's layout (for example *English (United States)*). ClipTyper then presses the keys that produce each character on that layout. Characters the layout cannot produce at all (such as `ä` on a US layout) are listed before typing starts, so a password is never typed wrong silently.

Switching between consoles: right-click the tray icon or the overlay button and pick **Keyboard layout** (choosing a layout also turns on VK Compatibility Mode) or toggle **VK Compatibility Mode**. In fullscreen RDP / KVM sessions the overlay menu is the one within reach.

### Overlay (fullscreen RDP / KVM)

Global hotkeys are often captured by the remote session. Enable **Show the overlay button** in **Settings → Overlay** (or toggle with **`Ctrl + Shift + H`**), hover the edge icon, click to type into the previously focused window.

---

## Settings

Double-click the tray icon (or right-click → **Settings**):

| Tab | Setting | Description | Default |
|---|---|---|---|
| Typing | **Type the clipboard** | Hotkey to start typing | `Ctrl + Alt + V` (installations from v1.7.0 or earlier keep `Ctrl + Shift + T`) |
| Typing | **Delay between keys** | 5–100 ms | 25 ms |
| Typing | **Humanized timing** | Random variance ±1–50 ms | Off (±5 ms) |
| Typing | **Line breaks** | `Enter`, `Shift + Enter`, `Space`, or `Ignore` | `Enter` |
| Typing | **Remove invisible characters** | Strip BOM, zero-width spaces, null bytes | On |
| Typing | **Ask before typing more than** | Confirm long texts | On (5,000 characters) |
| Keyboard | **Typing method** | *Characters (Unicode)* for local apps and RDP; *Key presses (VK Compatibility Mode)* for iLO, iDRAC, VMRC, Hyper-V, KVM | Characters |
| Keyboard | **Keyboard layout** | Layout of the console (key presses only); characters it cannot type are listed before typing. Caps Lock is switched off while typing and restored | Same as this PC |
| Credentials | **Mode** | Two-stage login for text with exactly two parts (`user<Tab>password`, line break, or custom delimiter) | Disabled |
| Credentials | **Custom delimiter** | Text between user name and password, then Tab or Enter | — |
| Credentials | **Pause before password** | 50–2000 ms | 200 ms |
| Credentials | **Clear the clipboard after** | Only if it still holds the typed credentials; Windows clipboard history is not cleared | Off (5 s) |
| Overlay | **Show the overlay button** | Floating button for fullscreen RDP / KVM | Off |
| Overlay | **Size / Monitor / Reset position** | 25–200%, display, default edge position | 100%, primary |
| Overlay | **Show or hide the overlay with a shortcut** | Toggle hotkey (can be disabled) | `Ctrl + Shift + H` |
| General | **Theme** | Auto (System), Light, Dark | Auto (System) |
| General | **Start ClipTyper with Windows** | *(Installed version)* | On |
| General | **Check for updates automatically** | GitHub check on launch (max 1× / 24 h) | On |
| General | **Play a sound when typing finishes** | | Off |
| General | **Write a diagnostic log** | Local `clip-typer.log`, never contains clipboard text; **Open folder** shows it | On |

---

## How it works

```
Clipboard text → SendInput (Unicode or VK keystrokes) → Focused window
```

- Background typing worker; UI stays responsive  
- Single typing session at a time  
- User-space only (HKCU autostart) — no admin required for normal installs  

---

## Building from source

```bash
git clone https://github.com/unpaved028/ClipTyper.git
cd ClipTyper
dotnet test ClipTyper.sln -c Release

# Portable (self-contained + portable.marker)
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o ./publish-portable
New-Item -Path ./publish-portable/portable.marker -ItemType File

# Slim (framework-dependent + portable.marker)
dotnet publish -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true -o ./publish-slim
New-Item -Path ./publish-slim/portable.marker -ItemType File

# Winget / installed mode (no marker; settings in AppData)
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o ./publish-winget

# Installer (Inno Setup)
iscc /DMyAppVersion=1.8.0 setup.iss
```

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

---

## Changelog

### v1.8.0
**New**
- Choose the keyboard layout of the remote console, so passwords are typed correctly even when the console uses a different layout (for example a US iLO console from a German PC)
- Before typing, ClipTyper lists characters that the console keyboard layout cannot produce, instead of typing them wrong
- Switch the typing method and the keyboard layout directly from the tray menu or the overlay menu, also inside fullscreen remote sessions
- Light and dark mode: ClipTyper follows the Windows setting, or choose Light or Dark in Settings
- A short welcome after installing, and a note about new features after an update

**Improved**
- A clearer Settings window with five tabs: Typing, Keyboard, Credentials, Overlay and General
- Modern Windows 11 style menus and crisp icons in the notification area, also on scaled displays
- Questions before typing are clearer, and affected characters stay hidden until you click "Show characters"
- Characters typed with dead keys (such as ^) or AltGr now come out correctly when typing key presses
- Caps Lock no longer inverts typed letters when typing key presses
- Better compatibility with consoles that read key positions (iLO, iDRAC, VMRC, KVM-over-IP)
- Sharper display on scaled screens (125 %, 150 % and more)
- Double-click the tray icon to open Settings
- The diagnostic log notes how each typing run was done (never the text), and Settings can open its folder for bug reports
- Easier to read update notes in the About dialog
- New default hotkey Ctrl + Alt + V for new installations, so browser and terminal shortcuts stay free; existing installations keep their hotkey

**Fixed**
- Hiding the overlay no longer closes ClipTyper
- The Portable and Slim versions now keep their settings next to the program and no longer add themselves to Windows startup
- The overlay keeps its size after a restart
- A hotkey that was taken by another app at startup is no longer lost when you save the settings
- Credential Auto-Type only reacts to text with exactly two parts, such as user name and password; other multi-line text is typed normally
- Clipboard auto-clear only clears the clipboard if it still contains the typed credentials
- Warnings while typing now appear as notifications, and questions appear in front of fullscreen remote sessions
- Text with old Mac-style line breaks keeps its line breaks
- Control characters in copied text are no longer typed, so they cannot trigger shortcuts in the target window
- Emoji and other special symbols are typed more reliably
- The Plain-Text Mode setting was removed because ClipTyper always types plain text
- Several smaller fixes for the tray icon, the update check, and the Settings and About windows

### v1.7.0
- ClipTyper now runs on the current supported .NET release (.NET 10)
- Settings are organized into Typing, Overlay, and App tabs with streamlined keyboard navigation
- Credential typing no longer writes how many characters were typed into the local log
- Clearer configuration for update checks and credential clipboard clearing delay

### v1.6.1
- Stop Typing from tray, overlay menu, or active overlay click
- Target compatibility matrix for RDP, Citrix, Horizon, iLO/iDRAC, serial consoles
- Security policy & zero-telemetry commitment (`SECURITY.md`)
- Portable read-only media fallback to LocalAppData
- Diagnostic logging toggle; clearer tray balloons
- Custom credential transition key (`Tab` / `Enter`)
- `SHA256SUMS.txt` on every release; hermetic tests & analysis cleanup

### v1.6.0
- Credential Auto-Type (Tab / Newline / custom delimiters)
- Credential safety (no password logging, focus check, optional clipboard clear)
- Configurable newlines, plain-text mode, typing jitter
- `TypingService` isolation and stability fixes

### v1.5.1
- Installer graceful shutdown while ClipTyper is running

### v1.5.0
- Escape / focus-loss abort, single-instance guard
- VK compatibility mode, input sanitization, long-text confirmation
- UIPI elevation warning, overlay/tray progress, rotating log

### v1.4.0
- Inno Setup installer, overlay scaling & multi-monitor, edge peek, overlay toggle hotkey

### v1.2.0
- Manual update check; blocked hotkey validation  
- **Deprecated for download — use [latest release](https://github.com/unpaved028/ClipTyper/releases/latest).**

---

## License

[MIT](LICENSE)
