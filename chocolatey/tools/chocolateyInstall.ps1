$ErrorActionPreference = 'Stop'

$packageName = 'velo'
$installerType = 'exe'
$url64 = 'https://github.com/badizher-codex/velo/releases/download/v2.0.0/VELO-2.0.0-Setup.exe'

# SHA256 of the signed Setup.exe — update this value for every release.
# Generate with: Get-FileHash VELO-2.0.0-Setup.exe -Algorithm SHA256
$checksum64     = 'PLACEHOLDER_SHA256_REPLACE_BEFORE_PUBLISH'
$checksumType64 = 'sha256'

$silentArgs = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-'

$packageArgs = @{
    packageName    = $packageName
    fileType       = $installerType
    url64bit       = $url64
    checksum64     = $checksum64
    checksumType64 = $checksumType64
    silentArgs     = $silentArgs
    validExitCodes = @(0)
}

Install-ChocolateyPackage @packageArgs
