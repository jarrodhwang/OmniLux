param([switch]$Test)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    & dotnet build OmniLux.slnx -c Release --nologo
    if ($LASTEXITCODE) { throw 'Build failed.' }
    if ($Test) {
        & dotnet run --project tests\OmniLux.Tests -c Release --no-build
        if ($LASTEXITCODE) { throw 'Core tests failed.' }
        & (Join-Path $PSScriptRoot 'Test-App.ps1') -Executable (Join-Path $root 'src\OmniLux\bin\Release\net10.0-windows\OmniLux.exe') -Demo
    }
} finally { Pop-Location }
