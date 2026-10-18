using Browser.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Browser.App;

public sealed partial class UiBridge
{
    private void BindCommands(WebView2 target, CoreWebView2 core, Action<Command> onCommand)
    {
        var tooltip = new ToolTip { PlacementTarget = target, Placement = Microsoft.UI.Xaml.Controls.Primitives.PlacementMode.Bottom };
        target.PointerExited += (_, _) => { tooltip.IsOpen = false; ToolTipService.SetToolTip(target, null); };
        target.Unloaded += (_, _) => tooltip.IsOpen = false;
        core.WebMessageReceived += (_, e) =>
        {
            string json;
            try { json = e.TryGetWebMessageAsString(); }
            catch (ArgumentException) { return; }
            if (!e.Source.StartsWith(Origin, StringComparison.Ordinal)) return;

            if (json == "\"ui.painted\"") { target.Opacity = 1; Painted?.Invoke(); return; }

            if (SidebarHost is { } host && json.Contains("\"sidebar.toggle\"", StringComparison.Ordinal))
            {
                host.Toggle();
                return;
            }
            if (CommandParser.Parse(json) is { } command)
            {
                if (command.Type == "sidebar.motion.done")
                {
                    // Anciennes vues : la disposition commune ne dépend plus de cette confirmation.
                    return;
                }
                if (PresentationCommand(target, command)) return;
                if (command.Type == "ui.tooltip")
                {
                    tooltip.IsOpen = false;
                    ToolTipService.SetToolTip(target, null);
                    if (command.Title is { Length: > 0 and <= 500 } text && command.X is { } x && command.Y is { } y &&
                        command.Width is > 0 && command.Height is > 0 && x >= 0 && y >= 0 && x < target.ActualWidth && y < target.ActualHeight)
                    {
                        tooltip.Content = new TextBlock { Text = text, MaxWidth = 300, TextWrapping = TextWrapping.Wrap };
                        tooltip.PlacementRect = new Windows.Foundation.Rect(x, y, Math.Min(command.Width.Value, target.ActualWidth - x), Math.Min(command.Height.Value, target.ActualHeight - y));
                        ToolTipService.SetToolTip(target, tooltip);
                        tooltip.IsOpen = true;
                        DebugLog.Write("ui.tooltip shown");
                    }
                    return;
                }
                tooltip.IsOpen = false;
                ToolTipService.SetToolTip(target, null);
                // Les commandes de la barre droite passent de sa vue locale aux coordonnées de la fenêtre.
                if (target == view && page == "index.html" && command.X is { } xLocal)
                {
                    var offset = target.TransformToVisual((UIElement)target.Parent).TransformPoint(new Windows.Foundation.Point(0, 0));
                    command = command with { X = xLocal + offset.X, Y = command.Y is { } yLocal ? yLocal + offset.Y : null };
                }
                if (command.Type is "ui.ready" or "dock.ready" or "network.ready" or "welcome.ready") ReplayTheme();
                onCommand(command);
                if (command.Type=="ui.ready") SidebarHost?.Publish();
            }
        };
    }
}
