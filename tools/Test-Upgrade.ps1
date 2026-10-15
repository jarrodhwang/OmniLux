param([string]$Setup, [string]$BaselineSetup)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$Setup) { $Setup = Join-Path $root 'dist\OmniLux-Setup.exe' }
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{CD3AAB20-7D7C-4F10-BEA8-A5A317833C76}_is1'
$registration = Get-ItemProperty -LiteralPath $key
$install = [IO.Path]::GetFullPath($registration.InstallLocation).TrimEnd('\')
$exe = Join-Path $install 'OmniLux.exe'
if (!(Test-Path -LiteralPath $exe)) { throw 'The registered OmniLux executable was not found.' }
$output = Join-Path $root ('build\validation\upgrade-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $output | Out-Null
$run = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\Microsoft\Windows\CurrentVersion\Run',$true)
$originalStartup = $run.GetValue('OmniLux',$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
$originalKind = if ($null -ne $originalStartup) { $run.GetValueKind('OmniLux') } else { [Microsoft.Win32.RegistryValueKind]::String }
$checks = [System.Collections.Generic.List[object]]::new()
function Check([string]$Name,[bool]$Pass) { $checks.Add(@{name=$Name;success=$Pass}); if (!$Pass) { throw "Upgrade check failed: $Name" } }
function Run-Setup([string]$Label,[string[]]$Extra = @(),[string]$SetupPath = $Setup) {
    $arguments = @('/SP-','/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="'+(Join-Path $output ($Label+'.log'))+'"')) + $Extra
    $process = Start-Process -FilePath $SetupPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(30000) -or $process.ExitCode) { throw "Upgrade installer failed: $Label" }
}
try {
    $owned = @(Get-Process OmniLux -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe })
    $stop = Start-Process -FilePath $exe -ArgumentList '--exit' -WindowStyle Hidden -PassThru
    if (!$stop.WaitForExit(10000)) { throw 'Cooperative exit command timed out.' }
    foreach ($process in $owned) { Check 'previous-instance-exits-cooperatively' ($process.WaitForExit(10000)) }
    $settingsPath = Join-Path $env:LOCALAPPDATA 'OmniLux\settings.json'
    $originalHash = if (Test-Path -LiteralPath $settingsPath) { (Get-FileHash -LiteralPath $settingsPath).Hash } else { $null }
    if ($BaselineSetup) {
        $startupTask = if ($originalStartup) { '/TASKS=startup' } else { '/TASKS=""' }
        Run-Setup -Label 'baseline-0.1' -Extra @($startupTask) -SetupPath $BaselineSetup
        Check 'baseline-version-is-0.1.0' ((Get-ItemProperty -LiteralPath $key).DisplayVersion -eq '0.1.0')
    }
    Run-Setup 'upgrade'
    $updated = Get-ItemProperty -LiteralPath $key
    $expectedVersion = ([xml](Get-Content (Join-Path $root 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
    Check 'installed-version-updated' ($updated.DisplayVersion -eq $expectedVersion)
    Check 'existing-install-path-preserved' ($updated.InstallLocation.TrimEnd('\') -eq $install)
    Check 'existing-startup-preference-preserved' ([bool]$run.GetValue('OmniLux') -eq [bool]$originalStartup)
    if ($originalHash) { Check 'existing-settings-bytes-preserved' ((Get-FileHash -LiteralPath $settingsPath).Hash -eq $originalHash) }
    else { Check 'upgrade-does-not-create-or-reset-settings' (!(Test-Path -LiteralPath $settingsPath)) }
    $manifest = Get-Content -LiteralPath (Join-Path $install 'manifest.json') -Raw | ConvertFrom-Json
    foreach ($file in $manifest.files) {
        $path = [IO.Path]::GetFullPath((Join-Path $install $file.path))
        if (!$path.StartsWith($install+'\',[StringComparison]::OrdinalIgnoreCase) -or (Get-FileHash -LiteralPath $path).Hash -ne $file.sha256) { throw "Installed payload differs: $($file.path)" }
    }
    Check 'all-installed-payload-hashes' $true
    $run.SetValue('OmniLux',('"'+$exe+'" --tray'),[Microsoft.Win32.RegistryValueKind]::String)
    Run-Setup 'enabled-startup-upgrade'
    Check 'enabled-startup-survives-upgrade' ($run.GetValue('OmniLux') -eq ('"'+$exe+'" --tray'))
    $run.DeleteValue('OmniLux',$false)
    Run-Setup 'disabled-startup-upgrade'
    Check 'disabled-startup-stays-disabled-after-previous-enabled-install' ($null -eq $run.GetValue('OmniLux'))
    Run-Setup 'explicit-startup-override' @('/TASKS=startup')
    Check 'explicit-startup-task-is-respected' ($run.GetValue('OmniLux') -eq ('"'+$exe+'" --tray'))
    if ($originalHash) { Check 'repeated-upgrades-preserve-settings-bytes' ((Get-FileHash -LiteralPath $settingsPath).Hash -eq $originalHash) }
} catch {
    $checks.Add(@{name='upgrade-exception';success=$false;error=$_.Exception.Message}); throw
} finally {
    if ($null -ne $originalStartup) { $run.SetValue('OmniLux',$originalStartup,$originalKind) }
    else { $run.DeleteValue('OmniLux',$false) }
    $run.Dispose()
    @{at=[DateTimeOffset]::UtcNow.ToString('o');checks=$checks.ToArray();failures=@($checks | Where-Object {!$_.success}).Count} |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'upgrade.json') -Encoding UTF8
}
Write-Output $output
