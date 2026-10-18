using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Browser.App;

// À largeur réduite, la discussion recouvre la page et garde toute la place disponible.
public sealed class ChatHost
{
    private readonly Grid _area;
    private readonly ColumnDefinition _column;
    private readonly WebView2 _view;
    private readonly UiBridge _bridge;
    private readonly Func<Task> _init;
    private Task? _initializing;
    public bool IsOpen { get; private set; }

    public ChatHost(Grid area, ColumnDefinition column, WebView2 view, UiBridge bridge, Func<Task> init)
    {
        (_area, _column, _view, _bridge, _init) = (area, column, view, bridge, init);
        area.SizeChanged += (_, _) => Rescale();
    }
    public void Rescale()
    {
        if (!IsOpen) return;
        var available = Math.Max(0, _area.ActualWidth);
        var scale = UiBridge.Settings?.UiFactor ?? 1;
        var overlay = available < 640 * scale;
        Grid.SetColumn(_view, overlay ? 0 : 1);
        Grid.SetColumnSpan(_view, overlay ? 2 : 1);
        _column.Width = new GridLength(overlay ? 0 : Math.Min(380 * scale, available * .48));
    }
    public async Task ShowAsync()
    {
        IsOpen = true; Rescale(); _view.Visibility = Visibility.Visible;
        try { await (_initializing ??= _init()); }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException or IOException)
        { _initializing = null; Hide(); DebugLog.Write("chat.init " + e.GetType().Name); }
    }
    public void Hide()
    {
        IsOpen = false; _column.Width = new GridLength(0); _view.Visibility = Visibility.Collapsed;
    }
    public void Post(string json) => _bridge.Post(json);
}
