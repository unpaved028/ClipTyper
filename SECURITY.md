# Security Policy

## Supported Versions
We currently provide security updates for the latest major release of ClipTyper.

## Reporting a Vulnerability
Please do not report security vulnerabilities through public GitHub issues. Instead, please report them privately via email to cliptyper-support.payroll373@passmail.com.

**A Note on Antivirus (False Positives):**
Because ClipTyper interacts directly with the Windows API (`SendInput`) and registers global hotkeys to simulate keystrokes, it may be flagged by some Endpoint Detection and Response (EDR) or Antivirus engines. This is a known heuristic limitation, not a security vulnerability in the code itself. We encourage all administrators to audit the transparent C# source code.
