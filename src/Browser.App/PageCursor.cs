using System.Globalization;
using System.Runtime.InteropServices;
using Windows.Foundation;

namespace Browser.App;

public sealed partial class PageHost
{
    public async Task<CursorWebView?> CursorHiddenAtAsync(Point position)
    {
        foreach (var id in _visible)
        {
            if (!_views.TryGetValue(id, out var view) || view.CoreWebView2 is not { } core || view.ActualWidth <= 0 || view.ActualHeight <= 0) continue;
            var local = view.TransformToVisual(null).Inverse.TransformPoint(position);
            if (local.X < 0 || local.Y < 0 || local.X >= view.ActualWidth || local.Y >= view.ActualHeight) continue;
            var x = (local.X / view.ActualWidth).ToString(CultureInfo.InvariantCulture);
            var y = (local.Y / view.ActualHeight).ToString(CultureInfo.InvariantCulture);
            try
            {
                var hide = await core.ExecuteScriptAsync($$"""
                    (() => {
                      let doc = document, x = {{x}} * innerWidth, y = {{y}} * innerHeight;
                      let el = doc.elementFromPoint(x, y);
                      for (let depth = 0; el && depth < 20; depth++) {
                        if (el.shadowRoot) {
                          const inner = el.shadowRoot.elementFromPoint(x, y);
                          if (inner && inner !== el) { el = inner; continue; }
                        }
                        if (el.tagName === 'IFRAME') {
                          try {
                            if (el.contentDocument) {
                              const r = el.getBoundingClientRect();
                              x -= r.left + el.clientLeft; y -= r.top + el.clientTop;
                              doc = el.contentDocument; el = doc.elementFromPoint(x, y); continue;
                            }
                          } catch {}
                        }
                        return doc.defaultView.getComputedStyle(el).cursor === 'none';
                      }
                      return false;
                    })()
                    """) == "true";
                return hide ? view as CursorWebView : null;
            }
            catch (Exception e) when (e is InvalidOperationException or COMException or ArgumentException) { return null; }
        }
        return null;
    }
}

public sealed class CursorWebView : Microsoft.UI.Xaml.Controls.WebView2
{
    private Microsoft.UI.Input.InputCursor? _previousCursor, _hiddenCursor;
    public void HideCursor(Microsoft.UI.Input.InputCursor cursor)
    {
        if (_hiddenCursor is null) _previousCursor = ProtectedCursor;
        _hiddenCursor = cursor;
        ProtectedCursor = cursor;
    }
    public void RestoreCursor()
    {
        if (_hiddenCursor is null) return;
        ProtectedCursor = _previousCursor ?? Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Arrow);
        _hiddenCursor = _previousCursor = null;
    }
}


