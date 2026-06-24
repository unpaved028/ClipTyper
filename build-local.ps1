$ErrorActionPreference = "Stop"

# Version to build
$version = "1.3.0"

# Kill any existing processes just in case
Stop-Process -Name "ClipTyper" -ErrorAction SilentlyContinue
Stop-Process -Name "ClipTyper-Portable" -ErrorAction SilentlyContinue
Stop-Process -Name "ClipTyper-Slim" -ErrorAction SilentlyContinue

function Publish-Variant {
    param (
        [string]$Name,
        [string]$OutputDir,
        [string]$TargetExeName,
        [bool]$SelfContained,
        [bool]$AddMarker
    )

    Write-Host "--- Publishing $Name ---" -ForegroundColor Cyan
    
    # Clean output directory
    if (Test-Path $OutputDir) {
        Remove-Item -Recurse -Force $OutputDir
    }
    
    # Run dotnet publish
    if ($SelfContained) {
        dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:Version=$version -o $OutputDir
    } else {
        dotnet publish -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true /p:Version=$version -o $OutputDir
    }

    # Add portable marker if requested
    if ($AddMarker) {
        $markerPath = Join-Path $OutputDir "portable.marker"
        New-Item -Path $markerPath -ItemType File -Value "This file marks ClipTyper as running in portable mode.`r`nSettings are stored next to this executable." -Force | Out-Null
    }

    # Rename with retry loop (to handle antivirus/indexer locking)
    $srcExe = Join-Path $OutputDir "ClipTyper.exe"
    
    # If target name is the same as source name, we don't need to rename
    if ($TargetExeName -ne "ClipTyper.exe") {
        Write-Host "Renaming $srcExe to $TargetExeName..."
        $retries = 10
        $renamed = $false
        while ($retries -gt 0) {
            try {
                if (Test-Path $srcExe) {
                    Rename-Item -Path $srcExe -NewName $TargetExeName -Force -ErrorAction Stop
                    $renamed = $true
                    break
                }
            } catch {
                Write-Host "File locked, retrying in 1s ($retries retries left)..." -ForegroundColor Yellow
                Start-Sleep -Seconds 1
                $retries--
            }
        }

        if (-not $renamed -and (Test-Path $srcExe)) {
            throw "Failed to rename $srcExe to $TargetExeName. The file is locked."
        }
    }

    Write-Host "Successfully published $Name to $OutputDir" -ForegroundColor Green
}

# 1. Portable
Publish-Variant -Name "Portable" -OutputDir "./publish-portable" -TargetExeName "ClipTyper-Portable.exe" -SelfContained $true -AddMarker $true

# 2. Slim
Publish-Variant -Name "Slim" -OutputDir "./publish-slim" -TargetExeName "ClipTyper-Slim.exe" -SelfContained $false -AddMarker $true

# 3. Winget
Publish-Variant -Name "Winget" -OutputDir "./publish-winget" -TargetExeName "ClipTyper.exe" -SelfContained $true -AddMarker $false
