$ErrorActionPreference = 'Stop'

# The installer is per-user, so the uninstall entry lives under HKCU (Get-UninstallRegistryKey checks it).
[array]$key = Get-UninstallRegistryKey -SoftwareName 'ClipTyper*'

if ($key.Count -eq 1) {
  $packageArgs = @{
    packageName    = $env:ChocolateyPackageName
    fileType       = 'exe'
    # The uninstaller closes a running ClipTyper itself when run silently
    silentArgs     = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'
    file           = "$($key[0].UninstallString)".Trim('"')
    validExitCodes = @(0)
  }
  Uninstall-ChocolateyPackage @packageArgs
}
elseif ($key.Count -eq 0) {
  Write-Warning "$($env:ChocolateyPackageName) has already been uninstalled by other means."
}
else {
  Write-Warning "$($key.Count) matches found. To prevent accidental data loss, no programs will be uninstalled."
  $key | ForEach-Object { Write-Warning "- $($_.DisplayName)" }
}
