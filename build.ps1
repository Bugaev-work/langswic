param([switch]$CompileOnly)
$ErrorActionPreference = 'Stop'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$wpf = Join-Path $framework 'WPF'
$out = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$zig = Join-Path $PSScriptRoot '.tools\compiler\zig-x86_64-windows-0.14.1\zig.exe'
if (-not (Test-Path -LiteralPath $zig)) { throw 'Run native\bootstrap.ps1 once to download and verify Zig 0.14.1. Subsequent builds work offline.' }
$nativeDll = Join-Path $out 'NativeLayout64.dll'
$env:ZIG_GLOBAL_CACHE_DIR = Join-Path $PSScriptRoot '.tools\cache'
$env:ZIG_LOCAL_CACHE_DIR = Join-Path $PSScriptRoot '.tools\local-cache'
& $zig cc -target x86_64-windows-gnu -shared -nostdlib -Os -s '-Wl,-e,DllMain' -o $nativeDll (Join-Path $PSScriptRoot 'native\layout.c') -luser32 -lkernel32
if ($LASTEXITCODE -ne 0) { throw 'Native input-language module build failed.' }
$nativeResource = '/resource:' + $nativeDll + ',FastSwitcher.NativeLayout64'
$assets = Join-Path $PSScriptRoot 'assets'
New-Item -ItemType Directory -Path $assets -Force | Out-Null
$iconPath = Join-Path $assets 'FastSwitcher.ico'
Add-Type -AssemblyName System.Drawing
$bitmap = New-Object System.Drawing.Bitmap(64, 64)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::Transparent)
$orange = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(245, 105, 27))
$white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
$font = New-Object System.Drawing.Font('Segoe UI', 34, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$graphics.FillEllipse($orange, 2, 2, 60, 60)
$graphics.DrawString('F', $font, $white, 18, 5)
$icon = [System.Drawing.Icon]::FromHandle($bitmap.GetHicon())
$stream = [System.IO.File]::Create($iconPath)
$icon.Save($stream)
$stream.Close()
$graphics.Dispose(); $orange.Dispose(); $white.Dispose(); $font.Dispose(); $bitmap.Dispose()
$references = @(
  (Join-Path $framework 'System.dll'),
  (Join-Path $framework 'System.Core.dll'),
  (Join-Path $framework 'System.Drawing.dll'),
  (Join-Path $framework 'System.Windows.Forms.dll'),
  (Join-Path $framework 'System.Web.Extensions.dll'),
  (Join-Path $wpf 'UIAutomationClient.dll'),
  (Join-Path $wpf 'UIAutomationTypes.dll')
)
$referenceArgs = @($references | ForEach-Object { '/reference:' + $_ })
$testReferenceArgs = @($referenceArgs + @(
  'PresentationFramework.dll','PresentationCore.dll','WindowsBase.dll','WindowsFormsIntegration.dll' |
    ForEach-Object { '/reference:' + (Join-Path $wpf $_) }
) + @(('/reference:' + (Join-Path $framework 'System.Xaml.dll')), ('/reference:' + (Join-Path $wpf 'UIAutomationProvider.dll'))))
$sources = @(Get-ChildItem $PSScriptRoot -Filter '*.cs' | ForEach-Object FullName)
& $csc /nologo /utf8output /platform:x64 /target:winexe ('/win32icon:' + $iconPath) ('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')) ('/out:' + (Join-Path $out 'FastSwitcher.exe')) $nativeResource $referenceArgs $sources
if ($LASTEXITCODE -ne 0) { throw 'Windows build failed.' }
& $csc /nologo /utf8output /define:INPUT_TEST /platform:x64 /target:exe ('/win32icon:' + $iconPath) ('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')) ('/out:' + (Join-Path $out 'FastSwitcher.Tests.exe')) $nativeResource $testReferenceArgs $sources
if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
if ($CompileOnly) { Write-Output 'Compilation complete; tests and installer were not run.'; exit 0 }
& (Join-Path $out 'FastSwitcher.Tests.exe') --self-test
if ($LASTEXITCODE -ne 0) { throw 'Self-tests failed.' }
& (Join-Path $out 'FastSwitcher.Tests.exe') --smoke-test
if ($LASTEXITCODE -ne 0) { throw 'UI smoke-test failed.' }
& (Join-Path $out 'FastSwitcher.Tests.exe') --input-test
if ($LASTEXITCODE -ne 0) { throw 'Input integration test failed.' }
Copy-Item -LiteralPath (Join-Path $out 'FastSwitcher.exe') -Destination (Join-Path $out 'FastSwitcher-Setup.exe') -Force
$package = Join-Path $out 'FastSwitcher-Setup.exe'
$hash = (Get-FileHash $package -Algorithm SHA256).Hash
Set-Content -LiteralPath (Join-Path $out 'SHA256SUMS.txt') -Value ($hash + '  FastSwitcher-Setup.exe') -Encoding Ascii
Get-FileHash $package -Algorithm SHA256 | Format-Table Hash,Path -AutoSize
