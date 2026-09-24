<#
.SYNOPSIS
    Retakes the README's two pictures, docs\screenshot.png and docs\options.png.

.DESCRIPTION
    Builds the demo repository afresh with make-demo-repo.ps1, runs the app against it in
    demo mode with a settings file of its own, and captures the main window and Options.

    The copy it runs is kept apart from the user's own: WORKTREEHELPER_SETTINGS gives it its
    own settings file, which also gives it its own single-instance lock, so a copy already
    running is neither read, written, nor brought forward. WORKTREEHELPER_DEMO=1 gives the
    picture what the demo repository cannot have for real - a pull request on duck-quack,
    and windows open on duck-bath and duck-quack - and leaves the update check out, so no
    notice of a newer release lands in it.

    The pictures come out at the display scaling of the machine running this, as a user of
    that machine would see the window. The README's are taken at 125%.

.PARAMETER Exe
    The app to photograph. By default dist\WorktreeHelper.exe, built by build.cmd.

.PARAMETER DemoRoot
    Where the demo repository is built, and so the path Options shows in its picture. The
    checkout's own demo folder by default; from a worktree of this repository that is a long
    path, and the main checkout's demo folder reads better.

.EXAMPLE
    tools\screenshots.ps1
    tools\screenshots.ps1 -Exe bin\Debug\net10.0-windows\WorktreeHelper.exe
    tools\screenshots.ps1 -DemoRoot D:\git\WorkTreeHelper\demo
#>
[CmdletBinding()]
param(
    [string]$Exe = (Join-Path $PSScriptRoot "..\dist\WorktreeHelper.exe"),
    [string]$DemoRoot = (Join-Path $PSScriptRoot "..\demo")
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if (-not (Test-Path $Exe)) { throw "No app at $Exe. Run build.cmd first, or pass -Exe." }
$Exe = (Resolve-Path $Exe).Path

Add-Type -AssemblyName System.Drawing, UIAutomationClient, UIAutomationTypes
Add-Type @"
using System; using System.Collections.Generic; using System.Runtime.InteropServices; using System.Text;
public static class ScreenshotWindows {
    public delegate bool Proc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(Proc p, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out Rect r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    public struct Rect { public int Left, Top, Right, Bottom; }

    /// The visible window of the process whose title starts so.
    public static IntPtr Find(uint pid, string titleStart) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, l) => {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p != pid || !IsWindowVisible(h)) return true;
            var t = new StringBuilder(256); GetWindowText(h, t, 256);
            if (t.ToString().StartsWith(titleStart)) found = h;
            return true; }, IntPtr.Zero);
        return found;
    }
}
"@
# Physical pixels, so the capture is the window as the screen has it rather than scaled down.
[ScreenshotWindows]::SetProcessDPIAware() | Out-Null

function Get-Bounds([IntPtr]$Window) {
    $r = New-Object ScreenshotWindows+Rect
    [ScreenshotWindows]::GetWindowRect($Window, [ref]$r) | Out-Null
    return $r
}

# Drawn by the window itself, so it comes out whole whatever is over it on screen.
function Save-Window([IntPtr]$Window, [string]$Path) {
    $r = Get-Bounds $Window
    $bitmap = New-Object System.Drawing.Bitmap(($r.Right - $r.Left), ($r.Bottom - $r.Top))
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $dc = $graphics.GetHdc()
    [ScreenshotWindows]::PrintWindow($Window, $dc, 2) | Out-Null # PW_RENDERFULLCONTENT
    $graphics.ReleaseHdc($dc); $graphics.Dispose()
    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    "  $([IO.Path]::GetFileName($Path)): $($bitmap.Width) x $($bitmap.Height)"
    $bitmap.Dispose()
}

function Wait-For([scriptblock]$Condition, [string]$What, [int]$Seconds = 30) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while ($clock.Elapsed.TotalSeconds -lt $Seconds) {
        $result = & $Condition
        if ($result) { return $result }
        Start-Sleep -Milliseconds 200
    }
    throw "Gave up waiting for $What."
}

# ---- the demo repository, in the state the pictures are of ------------------
& (Join-Path $PSScriptRoot "make-demo-repo.ps1") -Root $DemoRoot | Out-Null
$repo = Join-Path (Resolve-Path $DemoRoot).Path "rubber-duck"

# ---- a settings file of its own ---------------------------------------------
$version = ([Diagnostics.FileVersionInfo]::GetVersionInfo($Exe).ProductVersion -split '\+')[0]
$settings = Join-Path $root "demo\screenshot-settings.json"
[IO.File]::WriteAllText($settings, (@{
    LastRepoPath      = $repo
    AskedAboutTaskbar = $true        # no first-run question over the list
    LastSeenVersion   = $version     # and no what's-new
    AlignColumns      = $false       # the default layout
} | ConvertTo-Json), (New-Object System.Text.UTF8Encoding($false)))

$app = $null
$saved = @{ Settings = $env:WORKTREEHELPER_SETTINGS; Demo = $env:WORKTREEHELPER_DEMO }
try {
    $env:WORKTREEHELPER_SETTINGS = $settings
    $env:WORKTREEHELPER_DEMO = "1"
    $app = Start-Process $Exe -PassThru
    "Photographing $version, running as process $($app.Id)"

    $main = Wait-For { $h = [ScreenshotWindows]::Find([uint32]$app.Id, "Worktree Helper"); if ($h -ne [IntPtr]::Zero) { $h } } "the main window"

    # Settled: the list slides in once the first refresh is done, and the size then holds.
    $last = ""; $steady = [Diagnostics.Stopwatch]::StartNew()
    Wait-For {
        $r = Get-Bounds $main; $now = "$($r.Right - $r.Left)x$($r.Bottom - $r.Top)"
        if ($now -ne $last) { $script:last = $now; $steady.Restart() }
        ($r.Bottom - $r.Top) -gt 100 -and $steady.Elapsed.TotalSeconds -ge 2
    } "the window to settle" | Out-Null

    Save-Window $main (Join-Path $root "docs\screenshot.png")

    # Options, through its button in the caption.
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($main)
    $name = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "Options")
    $button = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $name)
    if (-not $button) { throw "No Options button in the main window." }
    $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()

    $options = Wait-For { $h = [ScreenshotWindows]::Find([uint32]$app.Id, "Options"); if ($h -ne [IntPtr]::Zero) { $h } } "Options"
    Start-Sleep -Milliseconds 800 # the icons beside the checkboxes, and the first paint
    Save-Window $options (Join-Path $root "docs\options.png")
}
finally {
    if ($app -and -not $app.HasExited) { Stop-Process -Id $app.Id -Force }
    $env:WORKTREEHELPER_SETTINGS = $saved.Settings
    $env:WORKTREEHELPER_DEMO = $saved.Demo
    Remove-Item $settings -ErrorAction SilentlyContinue
}
