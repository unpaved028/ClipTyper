# Security Policy & Threat Model

ClipTyper is designed from the ground up for use by IT professionals, system administrators, and security-conscious users. This document describes ClipTyper's security posture, threat model, and vulnerability reporting procedures.

---

## 🔒 Zero Telemetry & Privacy Commitment

* **Zero Telemetry:** ClipTyper collects, logs, and transmits **zero telemetry or analytics**.
* **Zero Cloud Services:** ClipTyper has no backend servers and relies on no cloud infrastructure.
* **Network Activity:** The **only** network interaction is an optional, throttled check against the public GitHub Releases API (`https://api.github.com/repos/unpaved028/ClipTyper/releases/latest`) to inform users of new releases. This can be disabled in Settings at any time.
* **No Credential Persistence:** Clipboard contents and auto-typed credentials are processed entirely in volatile memory and are **never** written to disk or configuration files. Exact character counts of credentials are intentionally excluded from logs.

---

## 🛡️ Threat Model

### What ClipTyper Does
* Reads the current Windows Clipboard content upon user trigger (global hotkey or overlay button click).
* Simulates standard keyboard input using the Windows `SendInput` API into the currently focused foreground window.
* Persists user preferences locally in a standard JSON file (`settings.json`).

### What ClipTyper Does NOT Do
* **Does NOT hook system-wide keystrokes:** ClipTyper is not a keylogger. It registers specific Windows global hotkeys via `RegisterHotKey` and does not install low-level keyboard hooks (`WH_KEYBOARD_LL`).
* **Does NOT monitor clipboard history:** ClipTyper reads the clipboard **only** when explicitly triggered by the user. It does not run clipboard listeners or retain clipboard history.
* **Does NOT bypass Windows security controls:** ClipTyper respects Windows User Interface Privilege Isolation (UIPI). A non-elevated ClipTyper process cannot inject keystrokes into elevated (Administrator) windows without user awareness.
* **Does NOT require Administrator privileges:** ClipTyper runs entirely in standard user space (Medium Integrity Level).

---

## 🛑 Windows Security & Isolation Boundaries

### User Interface Privilege Isolation (UIPI)
Windows prevents lower-integrity applications from sending input messages to higher-integrity windows. When ClipTyper detects that the active target window is elevated while ClipTyper is running as a standard user, it alerts the user with an elevation warning dialog.

### Credential Auto-Type Safety
When using two-stage credential typing:
1. ClipTyper performs an active focus verification check between Stage 1 (Username) and Stage 2 (Password) to ensure focus was not stolen.
2. Trailing line terminators are trimmed from the password stage to prevent unintended form submission.
3. An optional post-type clipboard auto-clear can wipe the clipboard after credential typing. It is off by default. When enabled, ClipTyper waits the configured delay before clearing (default 5 seconds, allowed range 1–60 seconds). The clipboard is only cleared if it still holds the typed text. Windows clipboard history (`Win+V`) and cloud clipboard sync are not cleared; turn them off if you copy credentials.

---

## 🔍 Antivirus & EDR Heuristic Detections (False Positives)

Because ClipTyper calls native Windows APIs (`SendInput`, `RegisterHotKey`, `AttachThreadInput`) to simulate raw keystrokes and restore window focus in RDP environments, some Endpoint Detection and Response (EDR) or Antivirus engines may flag it heuristically.

ClipTyper is 100% open-source C# with no obfuscation. We encourage security teams and administrators to review and build the source code directly:
* Reproducible build instructions are available in [CONTRIBUTING.md](CONTRIBUTING.md).
* Cryptographic release checksums (`SHA256SUMS.txt`) are provided with every GitHub release.
* VirusTotal (latest installer, v1.8.0): [1 of 69](https://www.virustotal.com/gui/file/d115c50fbdbea9e48f90871cf70c3650bb146c10521d086335d770b3b2ef48fc) (`d115c50fbdbea9e48f90871cf70c3650bb146c10521d086335d770b3b2ef48fc`), scanned 25 October 2026. Only Microsoft reports a detection, a machine-learning false positive (see below); the other 68 engines find nothing. Previous installer v1.7.0: [0 detections](https://www.virustotal.com/gui/file/bbaa17b32de4b381edbe98277b0a0a342331947b2e0f96c47d19276545a25c4c). Re-scan after each release; unsigned `SendInput` utilities can still trigger heuristic detections that are not malware.

### Known false positive: Microsoft Defender flags the v1.8.0 installer

* **Status (25 October 2026):** Microsoft Defender reports `Trojan:Win32/Wacatac.B!ml` (also shown as `Trojan:Win32/Wacatac.C!ml`) for `ClipTyper-Setup.exe` v1.8.0, SHA-256 `d115c50fbdbea9e48f90871cf70c3650bb146c10521d086335d770b3b2ef48fc`. On VirusTotal it is 1 of 69 engines; the other 68 find nothing.
* **Why:** the `!ml` suffix marks a verdict of Defender's machine-learning model, not a signature of known malware. New, unsigned programs that simulate keystrokes are a typical trigger. The v1.7.0 installer was not flagged.
* **What we did:** the file was submitted to Microsoft as a false positive on 25 October 2026. This section will be updated with Microsoft's answer.
* **Effect until then:** Defender can block the download, and `winget upgrade unpaved028.ClipTyper` fails because the installer is quarantined. Installed versions keep working.
* **What you can do:** wait for the update, or stay on v1.7.0. If you need v1.8.0 now, verify the file first: its SHA-256 must match `SHA256SUMS.txt` of the [v1.8.0 release](https://github.com/unpaved028/ClipTyper/releases/tag/v1.8.0). After that you can restore it in Windows Security → Virus & threat protection → Protection history. On managed devices, ask your security team to allow the hash.

---

## 📬 Reporting a Vulnerability

If you discover a potential security vulnerability in ClipTyper, please report it privately:

* **GitHub (preferred):** [Report a vulnerability](https://github.com/unpaved028/ClipTyper/security/advisories/new). Only you and the maintainer can see the report.
* **Email:** `cliptyper-support.payroll373@passmail.com`
* **Response SLA:** We commit to acknowledging receipt within 48 hours and providing regular remediation updates.
* **Public Disclosure:** Please do not open public GitHub issues for undisclosed vulnerabilities until a fix has been coordinated.
