using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Browser.App;

// Le composant beUI possède l'animation. WinUI ne modifie plus sa géométrie séparément.
public sealed class SidebarHost(ColumnDefinition column, Action<string> post)
{
    public const double MinWidth = 220, MaxWidth = 520, DefaultWidth = 296;
    public double Width { get; private set; } = DefaultWidth;
    public bool IsOpen { get; private set; } = true;
    public void Resize(double width)
    {
        var scale=UiBridge.Settings?.UiFactor??0.92;
        Width=Math.Clamp(Math.Round(width),MinWidth*scale,MaxWidth*scale);
        Publish();
    }
    public void Publish() => post(System.Text.Json.JsonSerializer.Serialize(new { @event="sidebar",open=IsOpen,width=Width }));
    public bool Toggle() { IsOpen=!IsOpen;Publish();return IsOpen; }
    public void Apply(double width) => column.Width = new GridLength(width);
}
