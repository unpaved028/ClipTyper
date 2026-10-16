# Scoop manifest

Installs `ClipTyper-Portable.exe` from GitHub Releases.

```powershell
scoop install https://raw.githubusercontent.com/unpaved028/ClipTyper/master/packaging/scoop/cliptyper.json
```

`scoop update cliptyper` later fetches this manifest again, so every release must update it on `master` (see `DEPLOY.md` checklist).

## How it works

- `ClipTyper-Portable.exe` runs in portable mode by its file name (v1.8.0+): settings live next to the EXE and nothing is registered for Windows startup.
- `persist: settings.json` keeps the settings across updates. ClipTyper overwrites a hard-linked `settings.json` in place (v1.8.0+), so Scoop's link survives saving.
- `pre_install` creates `{}` on the first install so Scoop persists a file, not a folder.
- `checkver` / `autoupdate` take the version from the latest GitHub release and the hash from `SHA256SUMS.txt`, so a bucket with Excavator (or `checkver.ps1 -u`) can bump the manifest automatically.

## Updating for a release

1. Set `version` and the `url` tag to the new version.
2. Set `hash` to the `ClipTyper-Portable.exe` line of the release's `SHA256SUMS.txt`.
3. Test: `scoop install .\packaging\scoop\cliptyper.json`, change a setting, `scoop update cliptyper --force`, check the setting survived.

## Later

A pull request to `ScoopInstaller/Extras` or an own bucket repo (template `ScoopInstaller/BucketTemplate`) can use this manifest unchanged.
