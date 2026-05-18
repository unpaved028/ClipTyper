# Contributing to ClipTyper

First off, thank you for considering contributing to ClipTyper! We welcome pull requests, bug reports, and feature suggestions from the community.

## 🐛 Found a Bug or Have a Feature Request?
Please use the provided Issue Templates in this repository. 
If you believe you have found a security vulnerability, please refer to our `SECURITY.md` and do not open a public issue.

## 🛠️ Local Development & Building from Source
Since ClipTyper interacts with the Windows API and relies on `SendInput`, we encourage everyone to compile the tool themselves for maximum transparency and security.

### Prerequisites
* [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### How to compile the portable executable
To build the lightweight, framework-dependent version (approx. 2MB) of ClipTyper, clone this repository and run the following command in the project root:

#### Portable (self-contained, single-file, compressed)
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:EnableCompressionInSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true

#### Slim (framework-dependent, requires .NET 8 Desktop Runtime)
dotnet publish -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true

The resulting ClipTyper.exe will be located in the \bin\Release\net8.0-windows\win-x64\publish\ directory.

## 🚀 Pull Requests
Fork the repository and create your branch from main.

Keep your changes focused. If you are adding a new feature, please explain the use case in your PR.

Make sure your code compiles without errors using the build command above.

Ensure the tool remains portable and does not require administrator privileges.

Thank you for helping make ClipTyper better!
