# Chocolatey package

Source of the `cliptyper` package on the Chocolatey Community Repository.

## How the package works

- Downloads the official `ClipTyper-Setup.exe` from GitHub Releases (nothing embedded, so no `VERIFICATION.txt`) and checks its SHA-256.
- Installs silently with `/NOLAUNCH` (v1.8.0+), so ClipTyper does not start after an unattended install. Winget is unaffected: without `/NOLAUNCH` a silent install still starts ClipTyper.
- The installer is per-user (`%LocalAppData%\Programs\ClipTyper`, HKCU autostart). Chocolatey usually runs elevated; in the normal UAC case this is still the same user.
- Uninstall runs the Inno uninstaller from the HKCU uninstall key. A silent uninstall closes a running ClipTyper first (v1.8.0+).

## Updating for a release

1. `cliptyper.nuspec`: `version`, `releaseNotes` link.
2. `tools/chocolateyinstall.ps1`: version in `url64bit`, `checksum64` from the `ClipTyper-Setup.exe` line of the release's `SHA256SUMS.txt`.
3. Test in a clean VM or Windows Sandbox from `packaging/chocolatey/cliptyper`:
   `choco pack`, then `choco install cliptyper -s . -y`, start ClipTyper, then `choco uninstall cliptyper -y`.
4. Push (maintainer account and API key):
   `choco push cliptyper.<version>.nupkg --source https://push.chocolatey.org/`
5. Moderation takes days to weeks; automated validation, verification and VirusTotal checks run first.
