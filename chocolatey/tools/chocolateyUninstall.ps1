$ErrorActionPreference = 'Stop'

$packageName    = 'velo'
$softwareName   = 'VELO Browser*'
$installerType  = 'exe'

# Inno Setup silent uninstall arguments
$silentArgs     = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'
$validExitCodes = @(0)

# Locate the uninstaller registered by the Inno Setup installer
[array]$key = Get-UninstallRegistryKey -SoftwareName $softwareName

if ($key.Count -eq 0) {
    Write-Warning "$packageName was not found in the registry — nothing to uninstall."
    return
}

if ($key.Count -gt 1) {
    Write-Warning "Found $($key.Count) entries for '$softwareName'. Uninstalling all."
}

$key | ForEach-Object {
    $file = $_.UninstallString -replace '/[Uu]ninstall', '' -replace '"', ''
    $file = $file.Trim()

    if (Test-Path $file) {
        Uninstall-ChocolateyPackage `
            -PackageName    $packageName `
            -FileType       $installerType `
            -SilentArgs     $silentArgs `
            -File           $file `
            -ValidExitCodes $validExitCodes
    } else {
        Write-Warning "Uninstaller not found at: $file"
    }
}
