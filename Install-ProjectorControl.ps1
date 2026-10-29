param(
    [Parameter(Mandatory = $true)][string]$DeviceSerial,
    [string]$ApkPath = (Join-Path $PSScriptRoot 'app/build/outputs/apk/debug/app-debug.apk')
)

# Explicit installation/setup for the owner's TV. No root or vendor service changes.
# WRITE_SECURE_SETTINGS is an optional ADB-granted permission; an APK cannot grant it itself.
$ErrorActionPreference = 'Stop'
function Invoke-AdbChecked {
    param([string[]]$AdbArguments)
    $result = @(& adb -s $DeviceSerial @AdbArguments 2>&1 | ForEach-Object { $_.ToString() })
    if ($LASTEXITCODE -ne 0) { throw ($result -join [Environment]::NewLine) }
    return $result
}

$apk = (Resolve-Path -LiteralPath $ApkPath).Path
$deviceState = (Invoke-AdbChecked @('get-state')) -join ''
if ($deviceState.Trim() -ne 'device') { throw 'Selected ADB device is not connected.' }
$currentUser = ((Invoke-AdbChecked @('shell', 'am', 'get-current-user')) -join '').Trim()
if ($currentUser -notmatch '^\d+$') { throw 'Could not identify the active Android user.' }

Invoke-AdbChecked @('install', '-r', $apk)
Invoke-AdbChecked @('shell', 'pm', 'grant', '--user', $currentUser, 'pl.screenreceiver.tv', 'android.permission.WRITE_SECURE_SETTINGS')
# This app-specific opt-in is also controlled by the app's STARTUP RECOVERY button.
Invoke-AdbChecked @('shell', 'settings', '--user', $currentUser, 'put', 'secure', 'screenreceiver_home_startup_recovery', '1')

$existing = ((Invoke-AdbChecked @('shell', 'settings', '--user', $currentUser, 'get', 'secure', 'enabled_accessibility_services')) -join '').Trim()
$component = 'pl.screenreceiver.tv/pl.screenreceiver.tv.shortcut.ProjectorShortcutAccessibilityService'
$components = @($existing.Split(':') | Where-Object { $_ -and $_ -ne 'null' })
if ($components -notcontains $component -and $components -notcontains 'pl.screenreceiver.tv/.shortcut.ProjectorShortcutAccessibilityService') {
    $components += $component
}
Invoke-AdbChecked @('shell', 'settings', '--user', $currentUser, 'put', 'secure', 'enabled_accessibility_services', ($components -join ':'))
Invoke-AdbChecked @('shell', 'settings', '--user', $currentUser, 'put', 'secure', 'accessibility_enabled', '1')
$readback = ((Invoke-AdbChecked @('shell', 'settings', '--user', $currentUser, 'get', 'secure', 'enabled_accessibility_services')) -join '').Trim()
if ($readback.Split(':') -notcontains $component -and $readback.Split(':') -notcontains 'pl.screenreceiver.tv/.shortcut.ProjectorShortcutAccessibilityService') {
    throw 'The TV replaced the saved shortcut selection. Inspect its accessibility state.'
}
Write-Output 'Projector Control installed; HOME shortcut enabled; startup recovery opted in.'
Write-Output 'Android may take a few seconds to bind the service. No reboot or projector command was sent.'
