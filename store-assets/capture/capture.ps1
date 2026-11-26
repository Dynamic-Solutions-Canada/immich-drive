# ImmichDrive's Store screenshots in one listing language (store-assets/README.md, "Screenshots"). ASCII only.
#
# Run by capture.sh through app_test_ui, in the guest's desktop session, after screenshot-desktop.ps1
# -Mode Prepare, with the dev package installed, the session proxy and the fake Immich server serving
# this locale's sample names, and Windows in this locale's display language. Reads its inputs from
# C:\apptest\capture-input.json (locale, scale) and the server address from sample-data.json, connects
# the app as a user would, and writes the raw captures with their sidecars to C:\apptest\out.
#
# Elements are found by automation ID (Find-ById) or by text that is the same in every language (the
# product name, the app's fixed-culture month folder name), never by translated text, so one script
# captures every locale. Sizes and offsets are given at 100% and multiplied by the scale, because the
# helpers work in physical pixels.
$ErrorActionPreference = 'Stop'
. C:\apptest\ui-lib.ps1

$inputs = Get-Content C:\apptest\capture-input.json -Raw | ConvertFrom-Json
$locale = $inputs.locale
$scale = [int]$inputs.scale / 100
$sample = (Get-Content C:\apptest\sample-data.json -Raw -Encoding UTF8 | ConvertFrom-Json).locales.$locale
if (-not $sample) { throw "sample-data.json has no entry for $locale" }

# The month folder name comes from the app in a fixed culture, so it reads the same in every language.
$monthName = '2026-08 August'
$driveRoot = 'C:\ImmichDrive'

<# A length at 100% display scale, in physical pixels at the capture's scale. #>
function Px([int]$Logical) { [int]($Logical * $scale) }

<# Click the screen at a physical position with the real mouse. #>
function Invoke-ClickAt([int]$X, [int]$Y) {
    [UiNative]::SetCursorPos($X, $Y) | Out-Null
    Start-Sleep -Milliseconds 200
    [UiNative]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
    [UiNative]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
}

<# Double-click the centre of an element with the real mouse. #>
function Invoke-DoubleClick($Element) {
    $r = $Element.Current.BoundingRectangle
    [UiNative]::SetCursorPos([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2)) | Out-Null
    Start-Sleep -Milliseconds 150
    foreach ($i in 1..2) {
        [UiNative]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
        [UiNative]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 60
    }
}

<#
Clear the keyboard focus rectangle that automation leaves, by clicking blank space at an offset (at
100%) from the window's top left, then park the pointer on the desktop's right edge so no hover shows.
#>
function Clear-Focus($Window, [int]$DX, [int]$DY) {
    $frame = Get-WindowFrame $Window
    Invoke-ClickAt ($frame.X + (Px $DX)) ($frame.Y + (Px $DY))
    Start-Sleep -Milliseconds 500
    [UiNative]::SetCursorPos((Px 1900), (Px 500)) | Out-Null
}

<# Select a navigation item of the settings window by its automation ID, and wait for its page. #>
function Select-Page($Window, [string]$AutomationId) {
    $item = Find-ById $Window $AutomationId
    if (-not $item) { throw "No navigation item $AutomationId" }
    Invoke-Click $item
    Start-Sleep -Seconds 3
}

<# The settings window: ImmichDrive's only top-level window wider than its tray flyout. #>
function Get-SettingsWindow {
    Get-AppWindows 'ImmichDrive' | Where-Object { $_.Current.BoundingRectangle.Width -gt (Px 400) } | Select-Object -First 1
}

# --- Check the guest, then start clean --------------------------------------------------------

# Windows itself must already be in this language; the app is set to it below.
$ui = Get-DisplayLanguage   # not Get-UICulture, which reads en-US for zh-CN and ja-JP in a console (ui-lib.ps1)
if ($ui -ne $locale) { throw "Windows is in $ui, not ${locale}; switch it first (capture.sh, CAPTURE_PREPARE_LOCALE)" }

New-Item -ItemType Directory -Force $UiOut | Out-Null
Get-ChildItem $UiOut -File | Where-Object Extension -in '.png', '.json' | Remove-Item
Get-Process ImmichDrive -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

# Synthetic settings, written before the first launch: the drive folder (so no account name appears
# in a path) and the app's own language, which matches Windows'.
$pkg = Get-AppxPackage -Name ImmichDrive
$dataDir = Join-Path $env:LOCALAPPDATA "Packages\$($pkg.PackageFamilyName)\LocalCache\Roaming\ImmichDrive"
New-Item -ItemType Directory -Force $dataDir | Out-Null
$settings = [ordered]@{ SyncRootPath = $driveRoot; Language = $locale }
[IO.File]::WriteAllText((Join-Path $dataDir 'settings.json'), ($settings | ConvertTo-Json), (New-Object Text.UTF8Encoding $false))

# Promote the tray icon out of the overflow for the throwaway guest, so the flyout shot shows it.
function Show-OwnTrayIcon {
    Get-ChildItem 'HKCU:\Control Panel\NotifyIconSettings' -ErrorAction SilentlyContinue | ForEach-Object {
        if ((Get-ItemProperty $_.PSPath).ExecutablePath -like '*ImmichDrive*') {
            Set-ItemProperty $_.PSPath -Name IsPromoted -Value 1 -Type DWord
        }
    }
}

# --- Connect, as a user would ------------------------------------------------------------------

Start-Process "shell:AppsFolder\$($pkg.PackageFamilyName)!App"
$window = $null
for ($i = 0; $i -lt 30 -and -not $window; $i++) { Start-Sleep -Seconds 2; $window = Get-SettingsWindow }
if (-not $window) { throw 'No ImmichDrive settings window' }
Set-WindowCentered $window (Px 1100) (Px 700)

Select-Page $window 'NavConnection'
$valuePattern = [System.Windows.Automation.ValuePattern]::Pattern
(Find-ById $window 'ServerBox').GetCurrentPattern($valuePattern).SetValue("https://$($sample.server)")
# A PasswordBox takes no ValuePattern: type the fake's test key (a test value, not a secret).
$key = Find-ById $window 'ApiKeyBox'
$key.SetFocus()
Invoke-Click $key
Start-Sleep -Milliseconds 300
Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.SendKeys]::SendWait('^a')
[System.Windows.Forms.SendKeys]::SendWait('{DEL}')
[System.Windows.Forms.SendKeys]::SendWait('test-key')
Start-Sleep -Seconds 1
(Find-ById $window 'ConnectButton').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
# The first listing and the placeholders take a while against the fake.
Start-Sleep -Seconds 25

# --- 00 Home and 01 Connection -----------------------------------------------------------------

Select-Page $window 'NavHome'
Start-Sleep -Seconds 15
Set-WindowCentered $window (Px 1100) (Px 700)
# The right end of the page title row is empty on every page.
Clear-Focus $window 1020 85
Save-DesktopCapture '00-home-dark' $window

Select-Page $window 'NavConnection'
Clear-Focus $window 1020 85
Save-DesktopCapture '01-connection-dark' $window

$window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
Start-Sleep -Seconds 2

# --- 04 Library: the drive's top folder in Details view -----------------------------------------

# A fresh Explorer window on Home, centred at the capture size. Found by class, not by its title,
# which is translated.
(New-Object -ComObject Shell.Application).Windows() | ForEach-Object { $_.Quit() }
Start-Sleep -Seconds 1
Start-Process explorer.exe
$explorer = Get-ExplorerWindows -TimeoutSeconds 15 | Select-Object -First 1
if (-not $explorer) { throw 'No File Explorer window' }
Set-WindowActive $explorer
Set-WindowCentered $explorer (Px 1200) (Px 700)
Start-Sleep -Seconds 1

# The drive's entry in the navigation pane, clicked as a user would, so the pane shows it selected
# (opening C:\ImmichDrive by path selects This PC instead). "ImmichDrive" is the product name in every
# language, but UI Automation often misses the pane (papercut 518), so fall back to its row: below
# Home, Gallery, the six pinned folders and a separator, the same rows in every language.
$nav = Find-ByName $explorer 'ImmichDrive' 'TreeItem'
if ($nav) {
    Invoke-Click $nav
} else {
    $frame = Get-WindowFrame $explorer
    Invoke-ClickAt ($frame.X + (Px 90)) ($frame.Y + (Px 476))
}
Start-Sleep -Seconds 5

$view = (New-Object -ComObject Shell.Application).Windows() | Select-Object -First 1
if ($view.Document.Folder.Self.Path -ne $driveRoot) { throw "Explorer shows $($view.Document.Folder.Self.Path), not $driveRoot" }
$view.Document.CurrentViewMode = 4   # Details
Start-Sleep -Seconds 3
Clear-Focus $explorer 1100 650
Save-DesktopCapture '04-library-dark' $explorer

# --- 02 Thumbnails: the month folder at a 160-pixel icon size ------------------------------------

$folder = Find-ByName $explorer $monthName 'ListItem'
if (-not $folder) { throw "No $monthName folder in the drive" }
Invoke-DoubleClick $folder
Start-Sleep -Seconds 4
$view = (New-Object -ComObject Shell.Application).Windows() | Select-Object -First 1
$view.Document.CurrentViewMode = 1   # Icons
$view.Document.IconSize = 160
# Thumbnails come from the provider over the network; give them time to draw.
Start-Sleep -Seconds 20
Clear-Focus $explorer 1100 650
Save-DesktopCapture '02-thumbnails-dark' $explorer
$view.Quit()
Start-Sleep -Seconds 2

# --- 03 Flyout: the tray flyout above the notification area -------------------------------------

Show-OwnTrayIcon
# The promotion takes effect when the icon is added again, so restart the app.
Get-Process ImmichDrive -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2
Start-Process "shell:AppsFolder\$($pkg.PackageFamilyName)!App"
Start-Sleep -Seconds 15
Get-SettingsWindow | ForEach-Object { $_.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() }
Start-Sleep -Seconds 2

# The tooltip starts with the product name in every language ("ImmichDrive: ..." or "ImmichDrive :").
$icon = Find-TrayIcon 'ImmichDrive'
if (-not $icon) { throw 'No ImmichDrive tray icon' }
Invoke-Click $icon
# Pointer off the tray, so the icon's tooltip does not cover the flyout.
[UiNative]::SetCursorPos((Px 400), (Px 300)) | Out-Null
Start-Sleep -Seconds 4
$flyout = Get-AppWindows 'ImmichDrive' | Where-Object { $_.Current.BoundingRectangle.Width -gt (Px 100) -and -not $_.Current.IsOffscreen } | Select-Object -First 1
if (-not $flyout) { throw 'No tray flyout' }
Save-DesktopCapture '03-flyout-dark' $flyout
[System.Windows.Forms.SendKeys]::SendWait('{ESC}')

Write-Output "capture finished: $locale"
