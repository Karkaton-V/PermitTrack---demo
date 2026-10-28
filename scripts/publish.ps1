# Rebuilds the release copy of PermitTrack and (re)creates the "PermitTrack Demo" desktop shortcut.
# Run from anywhere:  powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
# Demo data in app\App_Data is kept; delete that folder to reseed.

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$out = Join-Path $repo 'app'   # git-ignored

Get-Process PermitTrack.Web -ErrorAction SilentlyContinue | Stop-Process
dotnet publish (Join-Path $repo 'src\PermitTrack.Web') -c Release -o $out --nologo
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }
Copy-Item (Join-Path $PSScriptRoot 'Start-PermitTrack.ps1') $out -Force
Remove-Item (Join-Path $out 'PermitTrack.ico') -ErrorAction SilentlyContinue   # older builds kept the icon here

$desktop = [Environment]::GetFolderPath('Desktop')
Remove-Item (Join-Path $desktop 'PermitTrack.lnk') -ErrorAction SilentlyContinue   # shortcut name used by older builds
$shell = New-Object -ComObject WScript.Shell
$lnk = $shell.CreateShortcut((Join-Path $desktop 'PermitTrack Demo.lnk'))
$lnk.TargetPath = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
$lnk.Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$out\Start-PermitTrack.ps1`""
$lnk.WorkingDirectory = $out
$lnk.IconLocation = "$out\wwwroot\favicon.ico,0"
$lnk.Description = 'Open the PermitTrack demo as an app window or in your browser'
$lnk.WindowStyle = 7
$lnk.Save()

Write-Host "Published to $out and updated the 'PermitTrack Demo' desktop shortcut."
