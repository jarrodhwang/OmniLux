param(
    [string]$Executable,
    [switch]$Demo,
    [switch]$Hardware,
    [switch]$Startup,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$Executable) { $Executable = Join-Path $root 'src\OmniLux\bin\Release\net10.0-windows\OmniLux.exe' }
if (!$OutputDirectory) { $OutputDirectory = Join-Path $root ('build\validation\app-' + [Guid]::NewGuid().ToString('N')) }
$executablePath = [IO.Path]::GetFullPath($Executable)
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
function Run-AppCheck([string[]]$Arguments) {
    $process = Start-Process -FilePath $executablePath -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(30000)) {
        # Stop only the exact diagnostic process started by this script.
        Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
        throw 'Application diagnostic timed out.'
    }
    if ($process.ExitCode -ne 0) { throw "Application diagnostic exited with $($process.ExitCode)." }
}
$arguments = @('--smoke',('"' + $outputPath + '"'))
if ($Demo) { $arguments += '--demo' }
Run-AppCheck $arguments
$receipt = Get-Content -LiteralPath (Join-Path $outputPath 'smoke.json') -Raw | ConvertFrom-Json
if ($receipt.failures -ne 0) { throw 'UI smoke checks failed. Inspect smoke.json.' }
if ($Hardware) {
    $hardwareFile = Join-Path $outputPath 'hardware.json'
    Run-AppCheck @('--hardware-test',('"' + $hardwareFile + '"'))
    $hardwareReceipt = @(Get-Content -LiteralPath $hardwareFile -Raw | ConvertFrom-Json)
    if (!$hardwareReceipt.Count) { throw 'No adjustable DDC/CI hardware was found; hardware validation is incomplete.' }
    if (@($hardwareReceipt | Where-Object { !$_.ChangeVerified -or !$_.RestoreVerified }).Count) { throw 'DDC/CI change or restoration was not verified. Inspect hardware.json.' }
}
if ($Startup) {
    $startupFile = Join-Path $outputPath 'startup.json'
    Run-AppCheck @('--startup-test',('"' + $startupFile + '"'))
    $startupReceipt = Get-Content -LiteralPath $startupFile -Raw | ConvertFrom-Json
    if (!$startupReceipt.enabled -or !$startupReceipt.disabled -or !$startupReceipt.restored -or !$startupReceipt.quotesExecutable) { throw 'Startup registry round-trip failed.' }
}
Write-Output $outputPath
