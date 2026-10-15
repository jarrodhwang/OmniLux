param([ValidateSet('win-x64','win-arm64')][string]$Runtime = 'win-x64', [switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
$name = "OmniLux-$version-$Runtime"
$package = Join-Path $root "build\packages\$name"
if (Test-Path -LiteralPath $package) {
    $resolved = [IO.Path]::GetFullPath($package)
    $allowed = [IO.Path]::GetFullPath((Join-Path $root 'build\packages')) + '\'
    if (!$resolved.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -ne $name) { throw 'Unsafe package cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $package,(Join-Path $root 'dist') | Out-Null
if (!$SkipTests) { & (Join-Path $PSScriptRoot 'Build.ps1') -Test }
& dotnet publish (Join-Path $root 'src\OmniLux\OmniLux.csproj') -c Release -r $Runtime --self-contained true -o $package --nologo /p:DebugType=None /p:DebugSymbols=false
if ($LASTEXITCODE) { throw 'Self-contained publish failed.' }
$runtimeConfig = Get-Content -LiteralPath (Join-Path $package 'OmniLux.runtimeconfig.json') -Raw | ConvertFrom-Json
$frameworks = $runtimeConfig.runtimeOptions.includedFrameworks
$nugetDirectory = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget\packages' }
$licenseDirectory = Join-Path $package 'licenses'
New-Item -ItemType Directory -Force -Path $licenseDirectory | Out-Null
foreach ($framework in $frameworks) {
    $pack = Join-Path $nugetDirectory ($framework.name.ToLowerInvariant() + '.runtime.' + $Runtime + '\' + $framework.version)
    foreach ($notice in @('LICENSE','LICENSE.TXT','THIRD-PARTY-NOTICES.TXT')) {
        $source = Join-Path $pack $notice
        if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination (Join-Path $licenseDirectory ($framework.name + '-' + $notice)) -Force }
    }
}
if (@(Get-ChildItem -LiteralPath $licenseDirectory -File).Count -lt 3) { throw 'Runtime licenses and notices were not found in the restored runtime packs.' }
foreach ($file in @('README.md','README.ko.md','CHANGELOG.md','LICENSE','THIRD_PARTY_NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $root $file) -Destination $package -Force
}
Copy-Item -LiteralPath (Join-Path $root 'docs') -Destination $package -Recurse -Force
$files = Get-ChildItem -LiteralPath $package -File -Recurse | Where-Object { $_.Name -ne 'manifest.json' } | ForEach-Object {
    @{ path=$_.FullName.Substring($package.Length+1).Replace('\','/'); sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
}
@{ product='OmniLux'; version=$version; runtime=$Runtime; selfContained=$true; publisherSigned=$false; files=@($files) } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $package 'manifest.json') -Encoding UTF8
$archive = Join-Path $root "dist\$name.zip"
Compress-Archive -LiteralPath $package -DestinationPath $archive -Force
Write-Output $archive
Write-Output $package
