# Code signing policy

## Status

ClipTyper releases are **not code-signed yet**. We plan to sign them with free code signing from SignPath.io and a certificate issued by the SignPath Foundation. Once that is active, signed releases show "SignPath Foundation" as the publisher, and this page will state: "Free code signing provided by SignPath.io, certificate by SignPath Foundation".

Until then, verify a download with the SHA-256 checksums in `SHA256SUMS.txt`, which is attached to every [release](https://github.com/unpaved028/ClipTyper/releases).

## What gets signed

- Only files built from this repository: `ClipTyper.exe`, `ClipTyper-Portable.exe`, `ClipTyper-Slim.exe` and the installer `ClipTyper-Setup.exe`. We do not sign third-party software.
- Every release is built by GitHub Actions ([`build.yml`](.github/workflows/build.yml)) on GitHub-hosted runners from a tagged commit of the public source code. Nothing is signed on a local machine.
- Each signing request is approved manually by an approver (see below) before the release is published.

## Team roles

| Role | Members |
|---|---|
| Committers and reviewers | [@unpaved028](https://github.com/unpaved028) |
| Approvers | [@unpaved028](https://github.com/unpaved028) |

Contributions from others, such as pull requests, are reviewed by a committer before they are merged. Team members must use multi-factor authentication for GitHub and for SignPath.

## Privacy

ClipTyper does not collect, store or send personal data, and it has no telemetry. Clipboard contents never leave the computer.

Its only network access is the update check against the GitHub Releases API (`https://api.github.com/repos/unpaved028/ClipTyper/releases/latest`), at most once a day. It can be turned off in Settings → General. Details are in [SECURITY.md](SECURITY.md).

## What ClipTyper does

ClipTyper types clipboard text as keystrokes into windows where paste is not available, such as KVM and virtual machine consoles, HPE iLO and Dell iDRAC remote consoles, serial consoles and remote desktop sessions without a shared clipboard. It types only when the user presses its hotkey or clicks its button, it does not install a keyboard hook, and it cannot type into windows that run with higher privileges. The full description is in the [README](README.md).
