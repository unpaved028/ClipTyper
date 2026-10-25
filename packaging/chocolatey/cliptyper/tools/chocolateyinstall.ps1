$ErrorActionPreference = 'Stop'

$packageArgs = @{
  packageName    = $env:ChocolateyPackageName
  fileType       = 'exe'
  url64bit       = 'https://github.com/unpaved028/ClipTyper/releases/download/v1.8.0/ClipTyper-Setup.exe'
  checksum64     = 'd115c50fbdbea9e48f90871cf70c3650bb146c10521d086335d770b3b2ef48fc'
  checksumType64 = 'sha256'
  # Inno Setup, per-user. /NOLAUNCH: do not start ClipTyper after the unattended install.
  silentArgs     = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /NOLAUNCH'
  validExitCodes = @(0)
}

Install-ChocolateyPackage @packageArgs
