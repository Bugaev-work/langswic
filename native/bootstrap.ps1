$ErrorActionPreference = 'Stop'
$cache = Join-Path $PSScriptRoot '..\.tools'
$archive = Join-Path $cache 'zig-0.14.1.zip'
$compiler = Join-Path $cache 'compiler\zig-x86_64-windows-0.14.1\zig.exe'
if (Test-Path -LiteralPath $compiler) { Write-Output $compiler; exit 0 }
New-Item -ItemType Directory -Path $cache -Force | Out-Null
Invoke-WebRequest -UseBasicParsing -Uri 'https://ziglang.org/download/0.14.1/zig-x86_64-windows-0.14.1.zip' -OutFile $archive
$expected = '554f5378228923ffd558eac35e21af020c73789d87afeabf4bfd16f2e6feed2c'
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expected) { throw 'Zig archive checksum mismatch.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::ExtractToDirectory($archive, (Join-Path $cache 'compiler'))
Write-Output $compiler
