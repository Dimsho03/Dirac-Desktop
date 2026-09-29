# Offline Dirac Avalonia preview screenshots for a disposable Windows CI runner.
# Never reads the user's saved profiles and never invokes the live VPN.
[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\ui-artifacts'),
    [string]$ExecutablePath = (Join-Path $PSScriptRoot '..\v2rayN\v2rayN.Desktop\bin\Release\net10.0\v2rayN.exe')
)
$ErrorActionPreference = 'Stop'
$ExecutablePath = [IO.Path]::GetFullPath($ExecutablePath)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (!(Test-Path -LiteralPath $ExecutablePath)) { throw "Preview executable not found: $ExecutablePath" }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class DiracPreviewWin32 {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
}
'@ -ReferencedAssemblies @('System.Drawing')
$cases = @(
    @{ Name = 'home-550'; Width = 550; Height = 610; Page = 'home'; State = 'Connected' },
    @{ Name = 'home-500'; Width = 500; Height = 545; Page = 'home'; State = 'Connected' },
    @{ Name = 'routing-550'; Width = 550; Height = 610; Page = 'routing'; State = 'Connected' },
    @{ Name = 'settings-550'; Width = 550; Height = 610; Page = 'settings'; State = 'Disconnected' }
)
$beforeXray = @(Get-CimInstance Win32_Process -Filter "Name='xray.exe'" -ErrorAction SilentlyContinue | ForEach-Object { $_.ProcessId } | Sort-Object)
foreach ($case in $cases) {
    $argsList = @('--dirac-ui-preview', ('--dirac-preview-width=' + $case.Width), ('--dirac-preview-height=' + $case.Height), ('--dirac-preview-page=' + $case.Page), ('--dirac-preview-state=' + $case.State))
    $preview = Start-Process -FilePath $ExecutablePath -WorkingDirectory (Split-Path $ExecutablePath -Parent) -ArgumentList $argsList -PassThru -ErrorAction Stop
    try {
        $handle = [IntPtr]::Zero
        for ($i = 0; $i -lt 50; $i++) {
            Start-Sleep -Seconds 1
            $process = Get-Process -Id $preview.Id -ErrorAction SilentlyContinue
            if (!$process) { throw ('Preview process exited: ' + $case.Name) }
            if ($process.MainWindowHandle -ne [IntPtr]::Zero) { $handle = $process.MainWindowHandle; break }
        }
        if ($handle -eq [IntPtr]::Zero) { throw ('Preview window unavailable: ' + $case.Name) }
        [void][DiracPreviewWin32]::ShowWindow($handle, 4)
        Start-Sleep -Seconds 2
        $priorDpi = [DiracPreviewWin32]::SetThreadDpiAwarenessContext([IntPtr](-4))
        try {
            $rect = [DiracPreviewWin32+RECT]::new()
            if (![DiracPreviewWin32]::GetWindowRect($handle, [ref]$rect)) { throw ('GetWindowRect failed: ' + $case.Name) }
            $w = $rect.Right - $rect.Left
            $h = $rect.Bottom - $rect.Top
            if ($w -lt 470 -or $h -lt 500 -or $w -gt 2000 -or $h -gt 1800) { throw ('Unexpected preview size: ' + $case.Name + ' ' + $w + 'x' + $h) }
            $file = Join-Path $OutputDirectory ($case.Name + '.png')
            $bitmap = [System.Drawing.Bitmap]::new($w, $h)
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $device = $graphics.GetHdc()
                try { $ok = [DiracPreviewWin32]::PrintWindow($handle, $device, 2) }
                finally { $graphics.ReleaseHdc($device) }
                if (!$ok) { throw ('PrintWindow failed: ' + $case.Name) }
                $bitmap.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
            }
            finally { $graphics.Dispose(); $bitmap.Dispose() }
            Write-Output ('PREVIEW=' + $case.Name + ' SIZE=' + $w + 'x' + $h + ' BYTES=' + (Get-Item -LiteralPath $file).Length)
        }
        finally {
            if ($priorDpi -ne [IntPtr]::Zero) { [void][DiracPreviewWin32]::SetThreadDpiAwarenessContext($priorDpi) }
        }
    }
    finally {
        # Stop only the process launched above; never touch another Dirac instance.
        if (Get-Process -Id $preview.Id -ErrorAction SilentlyContinue) { Stop-Process -Id $preview.Id -Force -ErrorAction Stop }
    }
    $afterXray = @(Get-CimInstance Win32_Process -Filter "Name='xray.exe'" -ErrorAction SilentlyContinue | ForEach-Object { $_.ProcessId } | Sort-Object)
    if ([string]::Join(',', $beforeXray) -ne [string]::Join(',', $afterXray)) { throw ('Offline preview changed Xray process state: ' + $case.Name) }
}
Write-Output 'FOUR_OFFLINE_PREVIEW_IMAGES_CREATED=true'
