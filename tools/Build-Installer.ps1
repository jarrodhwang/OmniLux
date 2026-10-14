param([string]$PackageDirectory, [string]$Compiler)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
if (!$PackageDirectory) { $PackageDirectory = Join-Path $root "build\packages\OmniLux-$version-win-x64" }
if (!$Compiler) { $Compiler = Join-Path $PSScriptRoot 'cache\inno\7.1.0\compiler\ISCC.exe' }
if (!(Test-Path -LiteralPath $Compiler)) { throw 'Run tools\Restore-InstallerTools.ps1 first, or pass -Compiler.' }
$signature = Get-AuthenticodeSignature -LiteralPath $Compiler
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '^CN=Pyrsys B.V.,') { throw 'Unexpected installer compiler publisher.' }
$package = [IO.Path]::GetFullPath($PackageDirectory)
$manifest = Get-Content -LiteralPath (Join-Path $package 'manifest.json') -Raw | ConvertFrom-Json
if ($manifest.runtime -ne 'win-x64') { throw 'This installer packages the Windows x64 build.' }
foreach ($file in $manifest.files) {
    $path = [IO.Path]::GetFullPath((Join-Path $package $file.path))
    if (!$path.StartsWith($package + '\',[StringComparison]::OrdinalIgnoreCase) -or (Get-FileHash -LiteralPath $path).Hash -ne $file.sha256) { throw "Package validation failed: $($file.path)" }
}
& $Compiler /Qp ("/DPayloadDir=$package") ("/DProductVersion=$version") ("/DOutputDirectory=$(Join-Path $root 'dist')") (Join-Path $root 'installer\OmniLux.iss')
if ($LASTEXITCODE) { throw 'Installer compilation failed.' }
$setup = Join-Path $root "dist\OmniLux-$version-Setup.exe"
Copy-Item -LiteralPath $setup -Destination (Join-Path $root 'dist\OmniLux-Setup.exe') -Force
Get-ChildItem (Join-Path $root 'dist') -File | Where-Object { $_.Extension -in '.exe','.zip' } | ForEach-Object { '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant(),$_.Name } | Set-Content (Join-Path $root 'dist\SHA256SUMS.txt') -Encoding ASCII
Write-Output $setup
