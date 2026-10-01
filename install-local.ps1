$ErrorActionPreference = 'Stop'
$package = Join-Path $PSScriptRoot 'dist\FastSwitcher-Setup.exe'
$expectedHash = ((Get-Content -LiteralPath (Join-Path $PSScriptRoot 'dist\SHA256SUMS.txt') -Raw).Trim() -split '\s+')[0]
$packageHash = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash
if ($packageHash -ne $expectedHash) { throw 'Installer checksum does not match the completed build.' }
$installer = Start-Process -FilePath $package -ArgumentList '--setup-silent' -WindowStyle Hidden -Wait -PassThru
if ($installer.ExitCode -ne 0) { throw ('Installer failed: ' + $installer.ExitCode) }
$installed = Join-Path $env:LOCALAPPDATA 'Programs\FastSwitcher\FastSwitcher.exe'
$installedHash = (Get-FileHash -LiteralPath $installed -Algorithm SHA256).Hash
if ($installedHash -ne $packageHash) { throw 'Installed executable checksum differs from the package.' }
$version = (Get-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\FastSwitcher').DisplayVersion
$running = Start-Process -FilePath $installed -ArgumentList '--background' -WindowStyle Hidden -PassThru
Start-Sleep -Milliseconds 1200
if ($running.HasExited) { throw 'Installed application exited after startup.' }
$report = @(
    ('Version: ' + $version),
    'Installer exit: 0',
    'Hashes match: true',
    ('SHA256: ' + $packageHash),
    ('Installed: ' + $installed),
    ('Running process: ' + $running.Id)
)
$report | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'dist\install-verification.txt') -Encoding UTF8
$report
