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
$iconPath = Join-Path $assets 'langswic.ico'
$brandBuilder = Join-Path $out 'GenerateBrand.exe'
& $csc /nologo /utf8output /target:exe ('/out:' + $brandBuilder) ('/reference:' + (Join-Path $framework 'System.Drawing.dll')) (Join-Path $PSScriptRoot 'BrandLogo.cs') (Join-Path $assets 'GenerateBrand.cs')
if ($LASTEXITCODE -ne 0) { throw 'Brand assets compiler failed.' }
& $brandBuilder $assets
if ($LASTEXITCODE -ne 0) { throw 'Brand assets generation failed.' }
$brandResource = '/resource:' + $iconPath + ',Langswic.BrandIcon'
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
& $csc /nologo /utf8output /platform:x64 /target:winexe ('/win32icon:' + $iconPath) ('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')) ('/out:' + (Join-Path $out 'langswic.exe')) $nativeResource $brandResource $referenceArgs $sources
if ($LASTEXITCODE -ne 0) { throw 'Windows build failed.' }
& $csc /nologo /utf8output /define:INPUT_TEST /platform:x64 /target:exe ('/win32icon:' + $iconPath) ('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')) ('/out:' + (Join-Path $out 'langswic.Tests.exe')) $nativeResource $brandResource $testReferenceArgs $sources
if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
if ($CompileOnly) { Write-Output 'Compilation complete; tests and installer were not run.'; exit 0 }
& (Join-Path $out 'langswic.Tests.exe') --self-test
if ($LASTEXITCODE -ne 0) { throw 'Self-tests failed.' }
& (Join-Path $out 'langswic.Tests.exe') --smoke-test
if ($LASTEXITCODE -ne 0) { throw 'UI smoke-test failed.' }
& (Join-Path $out 'langswic.Tests.exe') --input-test
if ($LASTEXITCODE -ne 0) { throw 'Input integration test failed.' }
Copy-Item -LiteralPath (Join-Path $out 'langswic.exe') -Destination (Join-Path $out 'langswic-Setup.exe') -Force
$package = Join-Path $out 'langswic-Setup.exe'
$hash = (Get-FileHash $package -Algorithm SHA256).Hash
Set-Content -LiteralPath (Join-Path $out 'SHA256SUMS.txt') -Value ($hash + '  langswic-Setup.exe') -Encoding Ascii
Get-FileHash $package -Algorithm SHA256 | Format-Table Hash,Path -AutoSize
