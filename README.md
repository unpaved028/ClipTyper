# ClipTyper

A lightweight, portable Windows tool that types clipboard text as simulated keyboard input — character by character.

**Why?** Some environments block `Ctrl+V` paste (Remote Desktops, KVM consoles, web-based terminals, restricted VMs). ClipTyper bypasses this by simulating real keystrokes via the Windows `SendInput` API.

## ⬇️ Download

| Variante | Beschreibung | .NET Runtime nötig? | Download |
|---|---|---|---|
| **Portable** | Enthält die .NET Runtime – läuft sofort überall | ❌ Nein | **[ClipTyper-Portable.exe](https://github.com/unpaved028/ClipTyper/releases/latest/download/ClipTyper-Portable.exe)** |
| **Slim** | Kleinere Datei, benötigt installierte .NET 8 Runtime | ✅ [Runtime installieren](https://dotnet.microsoft.com/download/dotnet/8.0/runtime) | **[ClipTyper-Slim.exe](https://github.com/unpaved028/ClipTyper/releases/latest/download/ClipTyper-Slim.exe)** |

> 💡 **Unsicher welche Version?** Nimm **Portable** – die funktioniert auf jedem Windows 10/11 (x64) ohne weitere Installation.

## Features

- 🎯 **Paste anywhere** — works in RDP sessions, KVM consoles, web terminals, and password fields that block clipboard paste
- 📦 **Fully portable** — single `.exe`, no installation needed
- 🔒 **No admin rights** — runs entirely in user-space
- ⌨️ **Hardware-level input** — uses `SendInput` with Unicode characters, not `SendKeys`
- 🕐 **Reliable timing** — configurable delays ensure no characters are dropped
- 🖥️ **Silent background app** — runs as a system tray icon, no window

## Usage

1. **Start** `ClipTyper.exe` — it appears in the system tray (notification area)
2. **Copy** any text to your clipboard (`Ctrl+C`)
3. **Click** into the target window where you want to type
4. **Press** `Ctrl + Shift + T` — ClipTyper types the clipboard content character by character

> **Tip:** For passwords, copy the password first, then click into the password field and press `Ctrl+Shift+T`.

## How It Works

```
Clipboard text → SendInput (Unicode keystrokes) → Target application
```

ClipTyper reads text from the Windows clipboard and uses the native `SendInput` API with `KEYEVENTF_UNICODE` to simulate individual key presses. Each character is sent as a separate KeyDown/KeyUp pair with a 25ms delay between characters, ensuring reliable input even in slow or remote environments.

Before typing begins, all modifier keys (Ctrl, Shift, Alt) are programmatically released to prevent the hotkey combination from interfering with the typed text.

## System Requirements

- Windows 10/11 (x64)
- **Portable Version:** No additional requirements
- **Slim Version:** [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0/runtime) required
- No administrator privileges required

## Building from Source

```bash
# Clone
git clone https://github.com/unpaved028/ClipTyper.git
cd ClipTyper

# Portable (self-contained, compressed)
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:EnableCompressionInSingleFile=true

# Slim (framework-dependent, requires .NET 8 runtime)
dotnet publish -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true
```

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) for building.

## License

MIT
