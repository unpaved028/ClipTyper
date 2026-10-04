# Contributing to ClipTyper

First off, thank you for considering contributing to ClipTyper! We welcome pull requests, bug reports, and feature suggestions from the community.

## 🐛 Found a Bug or Have a Feature Request?
Please use the provided Issue Templates in this repository:
* [Bug Report](.github/ISSUE_TEMPLATE/bug_report.md)
* [Feature Request](.github/ISSUE_TEMPLATE/feature_request.md)
* [Target Compatibility Report](.github/ISSUE_TEMPLATE/target_compatibility.md)

If you believe you have found a security vulnerability, please refer to our [SECURITY.md](SECURITY.md) and report it privately.

## 🛠️ Local Development & Building from Source
Since ClipTyper interacts with the Windows API and relies on `SendInput`, we encourage everyone to compile the tool themselves for maximum transparency and security.

### Prerequisites
* [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

> [!NOTE]
> **Build Troubleshooting (TD-63):** If ClipTyper is currently running in your system tray or as a background process, make sure to **Exit** it before building locally to prevent file locking errors (`MSB3027`).

### Build Commands

#### 1. Automated Tests
Always verify that all automated unit tests pass before submitting changes:
```cmd
dotnet test ClipTyper.sln -c Release
```

#### 2. Slim Build (Framework-Dependent, ~540 KB)
Requires .NET 10 Desktop Runtime installed on the host machine:
```cmd
dotnet publish -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true
```

#### 3. Portable Build (Self-Contained, ~50 MB)
Fully self-contained single-file executable with zero runtime dependencies:
```cmd
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:EnableCompressionInSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
```

The resulting `ClipTyper.exe` will be located in `bin/Release/net10.0-windows/win-x64/publish/`.

## 🚀 Pull Requests
1. Fork the repository and create your branch from `master`.
2. Keep your changes focused. If you are adding a new feature, please explain the use case in your PR.
3. Make sure all unit tests pass (`dotnet test ClipTyper.sln -c Release`).
4. Ensure the tool remains lightweight, portable, and does not require administrator privileges.

Thank you for helping make ClipTyper better!
