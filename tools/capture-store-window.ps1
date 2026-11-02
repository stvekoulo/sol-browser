param([Parameter(Mandatory)][int]$ProcessId, [Parameter(Mandatory)][string]$Output)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class StoreWindowCapture {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
}
'@
$process = Get-Process -Id $ProcessId
$window = $process.MainWindowHandle
if ($window -eq [IntPtr]::Zero) { throw 'Fenêtre Sol introuvable.' }
$rect = [StoreWindowCapture+Rect]::new()
if (-not [StoreWindowCapture]::GetWindowRect($window, [ref]$rect)) { throw 'Dimensions introuvables.' }
$bitmap = [Drawing.Bitmap]::new($rect.Right - $rect.Left, $rect.Bottom - $rect.Top)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$dc = $graphics.GetHdc()
try {
    if (-not [StoreWindowCapture]::PrintWindow($window, $dc, 2)) { throw 'Capture native refusée.' }
} finally { $graphics.ReleaseHdc($dc); $graphics.Dispose() }
try { $bitmap.Save([IO.Path]::GetFullPath($Output), [Drawing.Imaging.ImageFormat]::Png) }
finally { $bitmap.Dispose() }
