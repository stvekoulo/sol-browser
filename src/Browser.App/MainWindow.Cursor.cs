using System.Runtime.InteropServices;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace Browser.App;

public sealed partial class MainWindow
{
    private CursorWebView? _hiddenCursorView;
    private InputCursor? _emptyCursor;
    private bool OwnCursor(Point point) => _shortcutsActive && GetAncestor(WindowFromPoint(point), 2) == _hwnd;

    private void RestorePageCursor()
    {
        _hiddenCursorView?.RestoreCursor();
        _hiddenCursorView = null;
    }

    private void WirePageCursor(PageHost pages)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        Point last = default;
        var movedAt = Environment.TickCount64;
        var busy = false;
        void Movement()
        {
            if (!GetCursorPos(out var point) || point.X == last.X && point.Y == last.Y) return;
            last = point; movedAt = Environment.TickCount64;
            RestorePageCursor();
        }
        Root.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler((_, _) => Movement()), true);
        Root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => { movedAt = Environment.TickCount64; RestorePageCursor(); }), true);
        Root.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, _) => { movedAt = Environment.TickCount64; RestorePageCursor(); }), true);
        Activated += (_, e) => { if (e.WindowActivationState == WindowActivationState.Deactivated) RestorePageCursor(); };
        Closed += (_, _) => { timer.Stop(); RestorePageCursor(); _emptyCursor?.Dispose(); };
        timer.Tick += async (_, _) =>
        {
            Movement();
            if (busy || !GetCursorPos(out var point)) return;
            if (!OwnCursor(point) || Root.XamlRoot is null) { RestorePageCursor(); return; }
            if (Environment.TickCount64 - movedAt < 3000) return;
            var client = point;
            if (!ScreenToClient(_hwnd, ref client)) return;
            var scale = Root.XamlRoot.RasterizationScale;
            var position = new Windows.Foundation.Point(client.X / scale, client.Y / scale);
            foreach (var overlay in new FrameworkElement[] { PopupFrame, CaptureFrame, DockFrame, Welcome })
            {
                if (overlay.Visibility != Visibility.Visible) continue;
                var local = overlay.TransformToVisual(Root).Inverse.TransformPoint(position);
                if (local.X >= 0 && local.Y >= 0 && local.X < overlay.ActualWidth && local.Y < overlay.ActualHeight)
                { RestorePageCursor(); return; }
            }
            busy = true;
            CursorWebView? hide;
            try { hide = await pages.CursorHiddenAtAsync(position); }
            finally { busy = false; }
            if (!GetCursorPos(out var current) || current.X != point.X || current.Y != point.Y || !OwnCursor(current) || Environment.TickCount64 - movedAt < 3000) return;
            if (hide != _hiddenCursorView) RestorePageCursor();
            if (hide is not null) { hide.HideCursor(_emptyCursor ??= EmptyCursor.Create()); _hiddenCursorView = hide; }
        };
        timer.Start();
    }

    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd, uint flags);
}
