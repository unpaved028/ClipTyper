$ErrorActionPreference = 'Stop'

$packageArgs = @{
  packageName    = $env:ChocolateyPackageName
  fileType       = 'exe'
  url64bit       = 'https://github.com/unpaved028/ClipTyper/releases/download/v1.8.0/ClipTyper-Setup.exe'
  checksum64     = 'REPLACE_WITH_SHA256_OF_ClipTyper-Setup.exe_FROM_SHA256SUMS.txt'
  checksumType64 = 'sha256'
  # Inno Setup, per-user. /NOLAUNCH: do not start ClipTyper after the unattended install.
  silentArgs     = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /NOLAUNCH'
  validExitCodes = @(0)
}

Install-ChocolateyPackage @packageArgs
