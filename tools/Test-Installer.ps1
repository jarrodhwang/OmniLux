param([string]$Setup)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
if (!$Setup) { $Setup = Join-Path $root 'dist\OmniLux-Setup.exe' }
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{CD3AAB20-7D7C-4F10-BEA8-A5A317833C76}_is1'
if (Test-Path $uninstallKey) { throw 'An existing OmniLux installation is registered. This test requires an unregistered test installation.' }
$validation = Join-Path $root ('build\validation\installer-' + [Guid]::NewGuid().ToString('N'))
$install = [IO.Path]::GetFullPath((Join-Path $validation 'app'))
if (!$install.StartsWith($root + '\build\validation\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Test installation must stay inside build\validation.' }
New-Item -ItemType Directory -Force -Path $validation | Out-Null
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$startupRegKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\Microsoft\Windows\CurrentVersion\Run',$true)
$previousStartup = $startupRegKey.GetValue('OmniLux',$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
$previousKind = if ($null -ne $previousStartup) { $startupRegKey.GetValueKind('OmniLux') } else { [Microsoft.Win32.RegistryValueKind]::String }
$checks = [System.Collections.Generic.List[object]]::new()
function Check([string]$Name, [bool]$Success) {
    $checks.Add(@{name=$Name;success=$Success})
    if (!$Success) { throw "Installer check failed: $Name" }
}
function Run-Installer([string]$Path, [string[]]$Arguments) {
    $process = Start-Process -FilePath $Path -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(30000)) { throw 'Installer timed out.' }
    if ($process.ExitCode) { throw "$([IO.Path]::GetFileName($Path)) exited with $($process.ExitCode)." }
    if ([IO.Path]::GetFileName($Path) -like 'unins*.exe') {
        # The original uninstaller exits before its TEMP clone finishes deleting it.
        $deadline = [DateTime]::UtcNow.AddSeconds(5)
        while ((Test-Path -LiteralPath $Path) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
    }
}
try {
    Run-Installer $Setup @('/SP-','/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/DIR="'+$install+'"'),'/TASKS=startup',('/LOG="'+(Join-Path $validation 'install.log')+'"'))
    $exe = Join-Path $install 'OmniLux.exe'
    Check 'application-installed' (Test-Path -LiteralPath $exe)
    Check 'uninstaller-registered' (Test-Path $uninstallKey)
    $installedManifest = Get-Content -LiteralPath (Join-Path $install 'manifest.json') -Raw | ConvertFrom-Json
    foreach ($file in $installedManifest.files) {
        $path = [IO.Path]::GetFullPath((Join-Path $install $file.path))
        if (!$path.StartsWith($install+'\',[StringComparison]::OrdinalIgnoreCase) -or (Get-FileHash -LiteralPath $path).Hash -ne $file.sha256) { throw "Installed payload differs: $($file.path)" }
    }
    Check 'installed-payload-hashes' $true
    Check 'startup-quotes-installed-executable' ($startupRegKey.GetValue('OmniLux') -eq ('"'+$exe+'" --tray'))
    & (Join-Path $PSScriptRoot 'Test-App.ps1') -Executable $exe -Demo -OutputDirectory (Join-Path $validation 'installed-ui')
    Check 'installed-ui-smoke' $true
    $process = Start-Process -FilePath $exe -ArgumentList '--tray' -WindowStyle Hidden -PassThru
    Start-Sleep -Milliseconds 1500
    Check 'tray-start-has-no-main-window' (!$process.HasExited -and $process.MainWindowHandle -eq 0)
    $second = Start-Process -FilePath $exe -WindowStyle Hidden -PassThru
    Check 'second-instance-exits' ($second.WaitForExit(10000) -and $second.ExitCode -eq 0)
    Start-Sleep -Milliseconds 400
    $process.Refresh()
    Check 'second-instance-reopens-first' ($process.MainWindowHandle -ne 0)
    Run-Installer $exe @('--exit')
    Check 'cooperative-exit' ($process.WaitForExit(10000))
    Run-Installer $Setup @('/SP-','/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/DIR="'+$install+'"'),'/TASKS=startup',('/LOG="'+(Join-Path $validation 'update.log')+'"'))
    Check 'update-keeps-installed-path' ((Get-ItemPropertyValue -Path $uninstallKey -Name InstallLocation).TrimEnd('\') -eq $install)
    Run-Installer (Join-Path $install 'unins000.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="'+(Join-Path $validation 'uninstall.log')+'"'))
    Check 'application-uninstalled' (!(Test-Path -LiteralPath $exe))
    Check 'uninstaller-registration-removed' (!(Test-Path $uninstallKey))
    Check 'startup-registration-removed' ($null -eq $startupRegKey.GetValue('OmniLux'))
} catch {
    $checks.Add(@{name='installer-exception';success=$false;error=$_.Exception.Message})
    throw
} finally {
    try {
        if ((Test-Path $uninstallKey) -and (Test-Path -LiteralPath (Join-Path $install 'unins000.exe'))) {
            Run-Installer (Join-Path $install 'unins000.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART')
        }
    } catch {
        $checks.Add(@{name='cleanup-exception';success=$false;error=$_.Exception.Message})
    }
    if ($null -ne $previousStartup) { $startupRegKey.SetValue('OmniLux',$previousStartup,$previousKind) }
    else { $startupRegKey.DeleteValue('OmniLux',$false) }
    $startupRegKey.Dispose()
    @{at=[DateTimeOffset]::UtcNow;checks=@($checks.ToArray());failures=@($checks | Where-Object {!$_.success}).Count} |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $validation 'installer.json') -Encoding UTF8
}
if (@($checks | Where-Object {!$_.success}).Count) { throw 'Installer validation failed. Inspect installer.json.' }
Write-Output $validation
