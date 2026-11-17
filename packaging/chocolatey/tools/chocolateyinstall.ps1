$ErrorActionPreference = 'Stop'

$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Definition

# Chocolatey creates shims for workfree.exe and workfree-gui.exe once they are unzipped into the tools folder.
# The .gui marker tells it the desktop app is a window, so its shim returns immediately.
New-Item -ItemType File -Path (Join-Path $toolsDir 'workfree-gui.exe.gui') -Force | Out-Null

Install-ChocolateyZipPackage `
  -PackageName 'workfree' `
  -Url64bit 'https://github.com/flowdevs-io/WorkFree/releases/download/vVERSION/workfree-win-x64.zip' `
  -Checksum64 'SHA256_WIN_X64' `
  -ChecksumType64 'sha256' `
  -UnzipLocation $toolsDir
