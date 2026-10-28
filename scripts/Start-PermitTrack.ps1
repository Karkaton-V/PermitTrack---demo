# Launches the PermitTrack demo. Asks whether to open it as a desktop app window or in the browser on
# localhost, starts the server if it isn't already running, then opens the chosen view.
#   App window: the server runs hidden and stops when you close the window (if this launch started it).
#   Browser:    the server runs in a minimized console window; close that window to stop it.
# Pass -Mode app or -Mode browser to skip the question.

param([ValidateSet('app', 'browser')][string]$Mode)

$ErrorActionPreference = 'Stop'
$port = 5080
$url = "http://localhost:$port"
$appDir = $PSScriptRoot
$icon = Join-Path $appDir 'wwwroot\favicon.ico'

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

function Show-LaunchChoice {
    $form = New-Object System.Windows.Forms.Form -Property @{
        Text = 'PermitTrack demo'; StartPosition = 'CenterScreen'; FormBorderStyle = 'FixedDialog'
        MaximizeBox = $false; MinimizeBox = $false; TopMost = $true; AutoScaleMode = 'Dpi'
        ClientSize = New-Object System.Drawing.Size 420, 192; BackColor = [System.Drawing.Color]::White
        Font = New-Object System.Drawing.Font 'Segoe UI', 9.5
    }
    if (Test-Path $icon) { $form.Icon = New-Object System.Drawing.Icon $icon }

    $title = New-Object System.Windows.Forms.Label -Property @{
        Text = 'How would you like to open PermitTrack?'; AutoSize = $true
        Location = New-Object System.Drawing.Point 20, 18
        Font = New-Object System.Drawing.Font 'Segoe UI Semibold', 12
    }
    $form.Controls.Add($title)

    $choices = @(
        @{ Key = 'app';     Y = 56;  Label = "Desktop app window`r`nOpens in its own window, like an installed program." },
        @{ Key = 'browser'; Y = 118; Label = "Web browser (localhost)`r`nOpens $url in your default browser." }
    )
    foreach ($c in $choices) {
        $btn = New-Object System.Windows.Forms.Button -Property @{
            Text = $c.Label; Tag = $c.Key; TextAlign = 'MiddleLeft'; FlatStyle = 'Flat'
            Location = New-Object System.Drawing.Point 20, $c.Y; Size = New-Object System.Drawing.Size 380, 54
            Padding = New-Object System.Windows.Forms.Padding 10, 0, 0, 0; Cursor = 'Hand'
        }
        $btn.FlatAppearance.BorderColor = [System.Drawing.Color]::FromArgb(31, 78, 121)
        $btn.FlatAppearance.MouseOverBackColor = [System.Drawing.Color]::FromArgb(232, 240, 248)
        $btn.Add_Click({ $form.Tag = $this.Tag; $form.Close() })
        $form.Controls.Add($btn)
    }
    $form.AcceptButton = $form.Controls[1]

    $cancel = New-Object System.Windows.Forms.Button -Property @{ DialogResult = 'Cancel'; Size = New-Object System.Drawing.Size 0, 0 }
    $form.Controls.Add($cancel); $form.CancelButton = $cancel   # Esc closes without launching

    [void]$form.ShowDialog()
    return $form.Tag
}

function Test-Up {
    try { (Invoke-WebRequest "$url/Account/Login" -UseBasicParsing -TimeoutSec 2).StatusCode -eq 200 } catch { $false }
}

function Start-Server([string]$windowStyle) {
    $proc = Start-Process -FilePath (Join-Path $appDir 'PermitTrack.Web.exe') -ArgumentList '--urls', $url `
        -WorkingDirectory $appDir -WindowStyle $windowStyle -PassThru
    $deadline = (Get-Date).AddSeconds(30)
    while (-not (Test-Up)) {
        if ($proc.HasExited -or (Get-Date) -gt $deadline) {
            [System.Windows.Forms.MessageBox]::Show("PermitTrack didn't start. Port $port may be in use by another program.",
                'PermitTrack demo', 'OK', 'Error') | Out-Null
            exit 1
        }
        Start-Sleep -Milliseconds 400
    }
    return $proc
}

# Edge (built into Windows) or Chrome can open a site as a chromeless app window.
function Find-AppBrowser {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
        "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe",
        "$env:ProgramFiles\Google\Chrome\Application\chrome.exe",
        "$env:LOCALAPPDATA\Google\Chrome\Application\chrome.exe"
    )
    $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
}

$choice = if ($Mode) { $Mode } else { Show-LaunchChoice }
if (-not $choice) { exit 0 }

if ($choice -eq 'browser') {
    if (-not (Test-Up)) { [void](Start-Server 'Minimized') }
    Start-Process $url
    exit 0
}

$browser = Find-AppBrowser
if (-not $browser) {
    [System.Windows.Forms.MessageBox]::Show("Couldn't find Microsoft Edge or Google Chrome for the app window, so PermitTrack will open in your browser instead.",
        'PermitTrack demo', 'OK', 'Information') | Out-Null
    if (-not (Test-Up)) { [void](Start-Server 'Minimized') }
    Start-Process $url
    exit 0
}

$server = if (Test-Up) { $null } else { Start-Server 'Hidden' }

# A dedicated profile makes the window its own process, so we can tell when it's closed.
$profileDir = Join-Path $env:LOCALAPPDATA 'PermitTrackDemo\AppWindow'
$window = Start-Process -FilePath $browser -PassThru -ArgumentList @(
    "--app=$url", "--user-data-dir=`"$profileDir`"", '--window-size=1280,860', '--no-first-run', '--no-default-browser-check'
)
$window.WaitForExit()

# The launcher process exits when its window closes, but the profile can keep helper processes briefly; wait for those too.
while (Get-CimInstance Win32_Process -Filter "Name = '$(Split-Path $browser -Leaf)'" |
        Where-Object { $_.CommandLine -like "*$profileDir*" }) {
    Start-Sleep -Seconds 1
}

if ($server -and -not $server.HasExited) { Stop-Process -Id $server.Id }
