---
name: Target Compatibility Report
about: Submit tested ClipTyper settings for a specific target environment or remote console
title: '[Target Compatibility] '
labels: 'compatibility'
assignees: ''

---

### Target Environment Summary
- **Target Type / Product:** [e.g. Proxmox 8.2 noVNC, VMware Horizon 2312, Supermicro IPMI Java console, Dell iDRAC 9 HTML5]
- **Target OS / Shell:** [e.g. Windows Server 2025, Debian 12 TTY, Cisco IOS-XE, VMware ESXi 8]
- **Client OS:** [e.g. Windows 11 23H2]
- **ClipTyper Version:** [e.g. 1.7.0]

---

### Working Configuration
Please list the settings that produced reliable typing without character drops or errors:

| Setting | Value Used |
|---|---|
| **Trigger Method** | `Hotkey (Ctrl+Shift+T)` / `Floating Overlay Button` |
| **Keystroke Delay (ms)** | [e.g. 25ms, 40ms, 60ms] |
| **VK Compatibility Mode** | `Enabled` / `Disabled` |
| **Plain-Text Mode** | `Enabled` / `Disabled` |
| **Newline Handling** | `Enter` / `Shift + Enter` / `Space` / `Ignore` |
| **Stage Pause (Credentials)** | [e.g. 200ms, 500ms (if applicable)] |

---

### Observations & Tips
- Did Unicode mode work directly or was VK Compatibility Mode required?
- Did character drops occur with default delay (25ms)? If so, what delay was stable?
- Any specific tips or caveats for other users (e.g. browser focus quirks, fullscreen hotkey capture)?
