# Records docs/demo.gif: builds throwaway demo repos, drives the real desktop app through a clean-up
# and captures the window. Windows only; needs git and ffmpeg on PATH and a published app in artifacts/win-x64.
#   pwsh tools/record-demo.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class Win {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int height, bool repaint);
}
'@
[Win]::SetProcessDPIAware() | Out-Null

$repo = Resolve-Path (Join-Path $PSScriptRoot '..')
$demo = Join-Path $repo 'artifacts\demo'
$code = Join-Path $demo 'code'
$frames = Join-Path $demo 'frames'
if (Test-Path $demo) { Remove-Item -Recurse -Force $demo -Confirm:$false }
New-Item -ItemType Directory -Force $code, $frames | Out-Null

# Demo repos. Each worktree gets an ignored node_modules holding a sparse file, so sizes look real without using disk.
function NewRepo($name) {
    $path = Join-Path $code $name
    git init -q -b main $path
    Set-Content (Join-Path $path 'README.md') "# $name"
    git -C $path add .
    git -C $path -c user.name=demo -c user.email=demo@example.com commit -q -m init
    Add-Content (Join-Path $path '.git\info\exclude') 'node_modules/'
    $path
}
function AddWorktree($repoPath, $branch, [long]$megabytes, [switch]$Dirty) {
    $path = Join-Path $code ((Split-Path $repoPath -Leaf) + '-' + $branch)
    git -C $repoPath worktree add -q $path -b $branch 2>&1 | Out-Null
    $modules = New-Item -ItemType Directory (Join-Path $path 'node_modules')
    $blob = Join-Path $modules 'deps.bin'
    New-Item -ItemType File $blob | Out-Null
    fsutil sparse setflag $blob | Out-Null
    $stream = [System.IO.File]::OpenWrite($blob); $stream.SetLength($megabytes * 1MB); $stream.Dispose()
    if ($Dirty) { Set-Content (Join-Path $path 'wip.txt') 'half-finished work' }
}
$shop = NewRepo 'shop-api'
$web = NewRepo 'web-app'
AddWorktree $shop 'agent-fix-login-timeout' 1840
AddWorktree $shop 'agent-refactor-billing' 912 -Dirty
AddWorktree $shop 'agent-add-rate-limits' 1310
AddWorktree $web 'agent-dark-mode' 2260
AddWorktree $web 'agent-upgrade-react' 1675
AddWorktree $web 'spike-new-checkout' 640
AddWorktree $web 'agent-fix-flaky-tests' 498

$env:WORKFREE_CONFIG = Join-Path $demo 'config.json'
Set-Content $env:WORKFREE_CONFIG (@{ roots = @($code) } | ConvertTo-Json)

$auto = [System.Windows.Automation.AutomationElement]
$tree = [System.Windows.Automation.TreeScope]
$proc = Start-Process (Join-Path $repo 'artifacts\win-x64\workfree-gui.exe') -PassThru
$script:frame = 0
$fps = 12

function Find($root, $name) {
    $cond = [System.Windows.Automation.PropertyCondition]::new($auto::NameProperty, $name)
    for ($i = 0; $i -lt 40; $i++) {
        $el = $root.FindFirst($tree::Descendants, $cond)
        if ($el) { return $el }
        Start-Sleep -Milliseconds 250
    }
    throw "not found: $name"
}
function Click($el) { $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }

# Captures the window's screen area for the given time at a steady frame rate.
function Record([int]$milliseconds) {
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    $taken = 0
    while ($clock.ElapsedMilliseconds -lt $milliseconds) {
        $bmp = [System.Drawing.Bitmap]::new($script:w, $script:h)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.CopyFromScreen($script:x, $script:y, 0, 0, $bmp.Size)
        $bmp.Save((Join-Path $frames ('f{0:d4}.png' -f $script:frame)))
        $g.Dispose(); $bmp.Dispose()
        $script:frame++; $taken++
        $wait = $taken * 1000 / $fps - $clock.ElapsedMilliseconds
        if ($wait -gt 0) { Start-Sleep -Milliseconds $wait }
    }
}

try {
    $win = $null
    for ($i = 0; $i -lt 40 -and -not $win; $i++) {
        Start-Sleep -Milliseconds 250
        $proc.Refresh()
        if ($proc.MainWindowHandle -ne 0) { $win = $auto::FromHandle($proc.MainWindowHandle) }
    }
    if (-not $win) { throw 'no window' }
    [Win]::MoveWindow($proc.MainWindowHandle, 80, 60, 1000, 640, $true) | Out-Null
    [Win]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
    Start-Sleep -Seconds 4
    $r = $win.Current.BoundingRectangle
    $script:x = [int]$r.X; $script:y = [int]$r.Y; $script:w = [int]$r.Width; $script:h = [int]$r.Height

    Record 1800
    Click (Find $win 'Select all safe')
    Record 1500
    Click (Find $win 'Delete selected')
    Record 1900

    $pidCond = [System.Windows.Automation.PropertyCondition]::new($auto::ProcessIdProperty, $proc.Id)
    $nameCond = [System.Windows.Automation.PropertyCondition]::new($auto::NameProperty, 'Delete')
    $button = $auto::RootElement.FindFirst($tree::Descendants, [System.Windows.Automation.AndCondition]::new($pidCond, $nameCond))
    if (-not $button) { throw 'no Delete button' }
    Click $button
    Record 11500
}
finally {
    if (-not $proc.HasExited) { $proc.CloseMainWindow() | Out-Null; Start-Sleep 1; if (-not $proc.HasExited) { $proc.Kill() } }
}

$docs = New-Item -ItemType Directory -Force (Join-Path $repo 'docs')
$gif = Join-Path $docs 'demo.gif'
ffmpeg -y -loglevel error -framerate $fps -i (Join-Path $frames 'f%04d.png') `
    -vf 'scale=860:-1:flags=lanczos,split[a][b];[a]palettegen=max_colors=96:stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=4:diff_mode=rectangle' $gif
"Wrote $gif ($([math]::Round((Get-Item $gif).Length / 1MB, 2)) MB, $script:frame frames)"
