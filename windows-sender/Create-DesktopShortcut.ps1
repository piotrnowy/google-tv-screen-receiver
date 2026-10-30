$ErrorActionPreference = 'Stop'

$publishDirectory = Join-Path $PSScriptRoot 'publish'
$executable = Join-Path $publishDirectory 'ScreenSender.Desktop.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "Published sender not found at '$executable'. Run dotnet publish first."
}

$desktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
$shortcutPath = Join-Path $desktop 'Projektor.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $executable
$shortcut.WorkingDirectory = $publishDirectory
$shortcut.Arguments = ''
$shortcut.Description = 'Stream desktop and system audio to Google TV'
$shortcut.IconLocation = "$executable,0"
$shortcut.Save()

Write-Host "Created desktop shortcut: $shortcutPath"
