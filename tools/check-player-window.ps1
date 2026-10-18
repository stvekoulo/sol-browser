param([int]$SolProcessId, [string]$Exe, [string]$DataDir, [string]$Url, [string]$File, [string]$Mode = 'resize')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
Add-Type @'
using System; using System.Runtime.InteropServices; using System.Collections.Generic; using System.Text;
public static class PlayerCheck {
  [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
  [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
  [StructLayout(LayoutKind.Sequential)] public struct CursorInfo { public int Size,Flags; public IntPtr Cursor; public Point Position; }
  [DllImport("user32.dll")] public static extern bool GetCursorInfo(ref CursorInfo cursor);
  public static CursorInfo ReadCursor() {
    var info=new CursorInfo{Size=Marshal.SizeOf<CursorInfo>()};
    if(!GetCursorInfo(ref info))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    return info;
  }
  [DllImport("user32.dll")] public static extern bool DrawIconEx(IntPtr dc,int x,int y,IntPtr icon,int w,int h,uint step,IntPtr brush,uint flags);
  public delegate bool WindowCallback(IntPtr h, IntPtr p);
  [DllImport("user32.dll")] public static extern bool EnumWindows(WindowCallback callback,IntPtr p);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h,int n);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h,out int p);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h,StringBuilder s,int n);
  public static IntPtr[] VideoWindows(int[] processes) {
    var result=new List<IntPtr>();
    EnumWindows((h,p)=>{int id;GetWindowThreadProcessId(h,out id);var name=new StringBuilder(256);GetClassName(h,name,256);
      if(Array.IndexOf(processes,id)>=0 && IsWindowVisible(h) && name.ToString()=="Chrome_WidgetWin_1" && (GetWindowLong(h,-20)&8)!=0) result.Add(h);return true;},IntPtr.Zero);
    return result.ToArray();
  }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Rect r);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr a,int x,int y,int w,int z,uint f);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point p);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,UIntPtr e);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int n);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
}
'@
$window = (Get-Process -Id $SolProcessId).MainWindowHandle
$previous = [PlayerCheck]::GetForegroundWindow()
$cursor = New-Object PlayerCheck+Point
[PlayerCheck]::GetCursorPos([ref]$cursor) | Out-Null
try {
  if ($Mode.StartsWith('cursor')) {
    [PlayerCheck]::SetWindowPos($window,[IntPtr](-1),30,30,1100,700,0x40) | Out-Null
    Start-Sleep -Milliseconds 400
    $rect = New-Object PlayerCheck+Rect
    [PlayerCheck]::GetWindowRect($window,[ref]$rect) | Out-Null
    $x = [int](($rect.Left + $rect.Right) / 2); $y = [int](($rect.Top + $rect.Bottom) / 2)
    [PlayerCheck]::SetCursorPos($x,$y) | Out-Null
    [PlayerCheck]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
    [PlayerCheck]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
    if ($Mode -eq 'cursor-sidebar') {
      $x=$rect.Left+70; $y=$rect.Top+170
      [PlayerCheck]::SetCursorPos($x,$y) | Out-Null
    }
    Start-Sleep -Milliseconds 300
    $info = [PlayerCheck]::ReadCursor()
    $beforeCursor = $info.Cursor
    Start-Sleep -Milliseconds 4500
    $info = [PlayerCheck]::ReadCursor()
    if ($info.Position.X -ne $x -or $info.Position.Y -ne $y) { throw "Pointer moved during native check: expected $x,$y actual $($info.Position.X),$($info.Position.Y)" }
    if ($Mode -in @('cursor-visible','cursor-sidebar')) {
      if (($info.Flags -band 1) -eq 0 -or $info.Cursor -ne $beforeCursor) { throw 'Pointer unexpectedly hidden outside playing video or on controls' }
      Write-Output "PASS real Windows pointer stays visible: $Mode"
      return
    }
    if (($info.Flags -band 1) -ne 0) {
      $bitmap = New-Object System.Drawing.Bitmap(64,64)
      $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
      $graphics.Clear([System.Drawing.Color]::Magenta)
      $dc = $graphics.GetHdc()
      try { if (-not [PlayerCheck]::DrawIconEx($dc,0,0,$info.Cursor,32,32,0,[IntPtr]::Zero,3)) { throw 'Cannot inspect native pointer image' } }
      finally { $graphics.ReleaseHdc($dc) }
      try {
        for ($px=0; $px -lt 32; $px++) { for ($py=0; $py -lt 32; $py++) {
          if ($bitmap.GetPixel($px,$py).ToArgb() -ne [System.Drawing.Color]::Magenta.ToArgb()) { throw 'Windows cursor remains visible after video idle (CSS alone is insufficient)' }
        } }
      } finally { $graphics.Dispose(); $bitmap.Dispose() }
    }
    if ($Mode -eq 'cursor-click') {
      [PlayerCheck]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
      [PlayerCheck]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
    } else { $x+=10; [PlayerCheck]::SetCursorPos($x,$y) | Out-Null }
    Start-Sleep -Milliseconds 200
    $info = [PlayerCheck]::ReadCursor()
    if ($info.Position.X -ne $x -or $info.Position.Y -ne $y) { throw 'Native pointer observation did not follow physical movement' }
    if (($info.Flags -band 1) -eq 0) { throw 'Windows cursor was not restored on movement' }
    if ($info.Cursor -ne $beforeCursor) { throw 'The original site cursor was not restored' }
    Write-Output "PASS real Windows pointer hides and restores the site cursor: $Mode"
  } elseif ($Mode -in @('resize','return','close')) {
    [PlayerCheck]::SetWindowPos($window,[IntPtr](-2),30,30,1100,700,0x40) | Out-Null
    $processes = @(Get-CimInstance Win32_Process | Select-Object ProcessId,ParentProcessId)
    $ids = @($SolProcessId)
    do {
      $children = @($processes | Where-Object { $_.ParentProcessId -in $ids -and $_.ProcessId -notin $ids } | ForEach-Object { [int]$_.ProcessId })
      $ids += $children
    } while ($children.Count)
    $windows = [PlayerCheck]::VideoWindows([int[]]$ids)
    if ($windows.Count -ne 1) { throw "Expected one native engine video window, found $($windows.Count)" }
    $pip = $windows[0]
    [PlayerCheck]::SetWindowPos($pip,[IntPtr](-1),40,40,400,250,0x40) | Out-Null
    Start-Sleep -Milliseconds 350
    if ($Mode -ne 'resize') {
      $rect = New-Object PlayerCheck+Rect
      [PlayerCheck]::GetWindowRect($pip,[ref]$rect) | Out-Null
      $scale = [PlayerCheck]::GetDpiForWindow($pip) / 96.0
      $x = if ($Mode -eq 'return') { $rect.Left + 80 * $scale } else { $rect.Right - 24 * $scale }
      [PlayerCheck]::SetCursorPos([int]$x,[int]($rect.Top + 24 * $scale)) | Out-Null
      Start-Sleep -Milliseconds 250
      [PlayerCheck]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
      [PlayerCheck]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
      Write-Output "PASS native $Mode button"
      return
    }
    $rect = New-Object PlayerCheck+Rect
    [PlayerCheck]::GetWindowRect($pip,[ref]$rect) | Out-Null
    $width = $rect.Right - $rect.Left
    $x = $rect.Left + 2; $y = [int](($rect.Top + $rect.Bottom) / 2)
    [PlayerCheck]::SetCursorPos($x,$y) | Out-Null
    Start-Sleep -Milliseconds 150
    [PlayerCheck]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
    Start-Sleep -Milliseconds 100
    for ($i=1; $i -le 10; $i++) {
      [PlayerCheck]::SetCursorPos($x - $i*5,$y) | Out-Null
      Start-Sleep -Milliseconds 35
    }
    [PlayerCheck]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
    Start-Sleep -Milliseconds 450
    [PlayerCheck]::GetWindowRect($pip,[ref]$rect) | Out-Null
    $grown = $rect.Right - $rect.Left
    if ($grown -lt $width + 30) { throw "Native edge did not resize: $width -> $grown" }
    $bitmap = New-Object System.Drawing.Bitmap($grown,($rect.Bottom - $rect.Top))
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($rect.Left,$rect.Top,0,0,$bitmap.Size)
    $bitmap.Save((Join-Path $DataDir 'pip-native.png'))
    $graphics.Dispose(); $bitmap.Dispose()
    Write-Output "PASS Edge native window edge resize $width -> $grown"
  } else {
    [PlayerCheck]::SetWindowPos($window,[IntPtr](-2),30,30,1100,700,0x40) | Out-Null
    $form = New-Object System.Windows.Forms.Form
    $form.Text = 'Sol external-open check'
    $form.Size = New-Object System.Drawing.Size(300,120)
    $form.Show()
    foreach ($target in @($Url,$File,'')) {
      [PlayerCheck]::ShowWindow($window,6) | Out-Null
      $form.Activate()
      [System.Windows.Forms.Application]::DoEvents()
      [PlayerCheck]::SetForegroundWindow($form.Handle) | Out-Null
      $env:NAVIGATEUR_DATA_DIR = $DataDir
      $child = Start-Process -FilePath $Exe -ArgumentList (@('--profile=robustness') + $(if ($target) { '"' + $target + '"' })) -WindowStyle Hidden -PassThru
      $child.WaitForExit(10000) | Out-Null
      $end = [DateTime]::UtcNow.AddSeconds(8)
      while ([DateTime]::UtcNow -lt $end -and ([PlayerCheck]::GetForegroundWindow() -ne $window -or [PlayerCheck]::IsIconic($window))) {
        [System.Windows.Forms.Application]::DoEvents()
        Start-Sleep -Milliseconds 100
      }
      if ([PlayerCheck]::GetForegroundWindow() -ne $window -or [PlayerCheck]::IsIconic($window)) { throw "External open failed to restore/foreground Sol: $target" }
    }
    Write-Output 'PASS external link, file and empty launch restore Sol to foreground'
  }
} finally {
  [PlayerCheck]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
  if ($form) { $form.Close(); $form.Dispose() }
  [PlayerCheck]::SetWindowPos($window,[IntPtr](-2),-9000,-9000,1100,800,0x10) | Out-Null
  [PlayerCheck]::SetCursorPos($cursor.X,$cursor.Y) | Out-Null
  [PlayerCheck]::SetForegroundWindow($previous) | Out-Null
}
