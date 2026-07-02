<#
.SYNOPSIS
    UI Automation smoke test for the AOT/R2R-published OneMMC shell: selects every
    top-level NavigationView item and verifies the process stays alive and responsive.

.DESCRIPTION
    Used for the M1 migration gate ("AOT-published app boots to the main window and
    navigates all top-level pages", doc/NativeAotMigration.md) and reusable for the
    M2-M4 per-feature smoke tests. Drives an ALREADY-RUNNING OneMMC instance through
    the AutomationIds defined in MainWindow.xaml (Nav_* items + the built-in
    SettingsItem), capturing a per-page screenshot into -OutDir.

    Must run in Windows PowerShell 5.1 (powershell.exe): the UIAutomationClient /
    UIAutomationTypes assemblies resolve from the .NET Framework GAC, which pwsh
    (PowerShell 7+) cannot load.

.EXAMPLE
    Start-Process src/OneMMC/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/publish/OneMMC.exe
    powershell.exe -NoProfile -File eng/Drive-AotSmokeNavigation.ps1 -OutDir eng/aot-logs/nav-shots
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing

# CopyFromScreen needs physical-pixel coordinates; UIA reports physical pixels, so the
# script process itself must be DPI-aware or captures are offset on scaled displays.
$sig = @'
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr hWnd);
'@
$U32 = Add-Type -MemberDefinition $sig -Name 'U32' -Namespace 'Native' -PassThru
$U32::SetProcessDPIAware() | Out-Null

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

function Find-Window {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, 'OneMMC')
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
}

function Save-Shot([System.Windows.Automation.AutomationElement]$win, [string]$path) {
    $rect = $win.Current.BoundingRectangle
    $bmp = New-Object System.Drawing.Bitmap([int]$rect.Width, [int]$rect.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen([int]$rect.X, [int]$rect.Y, 0, 0, $bmp.Size)
    $bmp.Save($path)
    $g.Dispose(); $bmp.Dispose()
}

function Invoke-NavItem([System.Windows.Automation.AutomationElement]$win, [string]$autoId) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $autoId)
    $item = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if ($null -eq $item) { return "NOTFOUND" }
    $pattern = $null
    # NavigationViewItem peers expose SelectionItem; Select() raises ItemInvoked, which is
    # where MainWindow performs the actual page navigation.
    if ($item.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pattern)) {
        $pattern.Select()
        return "SELECTED"
    }
    if ($item.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) {
        $pattern.Invoke()
        return "INVOKED"
    }
    return "NOPATTERN"
}

$win = Find-Window
if ($null -eq $win) { Write-Output 'FATAL: OneMMC window not found (launch the app first)'; exit 1 }
$hwnd = [System.IntPtr]$win.Current.NativeWindowHandle
$U32::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Milliseconds 500

$proc = Get-Process -Name OneMMC -ErrorAction SilentlyContinue
if ($null -eq $proc) { Write-Output 'FATAL: OneMMC process not found'; exit 1 }

# Keep in sync with the AutomationIds in src/OneMMC/MainWindow.xaml.
$pages = @(
    @{ Id = 'Nav_PolicyPolicies';         Shot = 'nav-2-policy.png' },
    @{ Id = 'Nav_CertificatesCredential'; Shot = 'nav-3-certs.png' },
    @{ Id = 'Nav_UserSecurity';           Shot = 'nav-4-usersec.png' },
    @{ Id = 'Nav_PrintManagement';        Shot = 'nav-5-print.png' },
    @{ Id = 'Nav_SystemManagement';       Shot = 'nav-6-sysmgmt.png' },
    @{ Id = 'SettingsItem';               Shot = 'nav-7-settings.png' },
    @{ Id = 'Nav_PCManagement';           Shot = 'nav-8-pcmgmt-return.png' }
)

$failed = $false
foreach ($page in $pages) {
    $result = Invoke-NavItem $win $page.Id
    Start-Sleep -Milliseconds 2500
    $proc.Refresh()
    $alive = -not $proc.HasExited
    $responding = if ($alive) { $proc.Responding } else { $false }
    Write-Output ("{0}: invoke={1} alive={2} responding={3}" -f $page.Id, $result, $alive, $responding)
    if (-not $alive) {
        Write-Output ("EXITCODE=0x{0:X8}" -f $proc.ExitCode)
        $failed = $true
        break
    }
    if (-not $responding -or $result -eq 'NOTFOUND' -or $result -eq 'NOPATTERN') { $failed = $true }
    Save-Shot $win (Join-Path $OutDir $page.Shot)
}

if ($failed) { Write-Output 'RESULT: FAIL'; exit 1 }
Write-Output 'RESULT: PASS'
