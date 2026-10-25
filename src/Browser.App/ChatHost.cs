using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Browser.Core;

namespace Browser.App;

// À largeur réduite, la discussion recouvre la page tout en gardant sa largeur plafonnée.
public sealed class ChatHost
{
    private readonly Grid _area;
    private readonly ColumnDefinition _column;
    private readonly WebView2 _view;
    private readonly DividerGrip _grip;
    private readonly Settings _settings;
    private readonly UiBridge _bridge;
    private readonly Func<Task> _init;
    private Task? _initializing;
    public bool IsOpen { get; private set; }

    public const double DefaultWidth = SidePanelLayout.ChatDefault;
    public ChatHost(Grid area, ColumnDefinition column, WebView2 view, DividerGrip grip, UiBridge bridge, Func<Task> init, Settings settings)
    {
        (_area, _column, _view, _bridge, _init) = (area, column, view, bridge, init);
        (_grip, _settings) = (grip, settings);
        area.SizeChanged += (_, _) => Rescale();
    }
    public void Resize(double width)
    {
        _settings.ChatWidth = width / _settings.UiFactor;
        Rescale();
    }
    public void Rescale()
    {
        if (!IsOpen) return;
        var available = Math.Max(0, _area.ActualWidth);
        var scale = _settings.UiFactor;
        var overlay = available < 640 * scale;
        Grid.SetColumn(_view, overlay ? 0 : 1);
        Grid.SetColumnSpan(_view, overlay ? 2 : 1);
        // Garder une page utilisable et restaurer la préférence quand la fenêtre retrouve assez de place.
        var width = SidePanelLayout.Chat(_settings.ChatWidth * scale, available, scale, overlay);
        _column.Width = new GridLength(overlay ? 0 : width);
        _view.Width = overlay ? width : double.NaN;
        _view.HorizontalAlignment = overlay ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
        _grip.Visibility = overlay ? Visibility.Collapsed : Visibility.Visible;
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
        IsOpen = false; _column.Width = new GridLength(0); _view.Visibility = Visibility.Collapsed; _grip.Visibility = Visibility.Collapsed;
    }
    public void Post(string json) => _bridge.Post(json);
}
