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

            if (json == "\"ui.painted\"") { target.Opacity = 1; return; }

            if (SidebarHost is { } host && json.Contains("\"sidebar.toggle\"", StringComparison.Ordinal))
            {
                var open = host.Toggle();
                view.CoreWebView2?.PostWebMessageAsJson($$"""{"event":"sidebar","open":{{(open ? "true" : "false")}}}""");
                return;
            }
            if (CommandParser.Parse(json) is { } command)
            {
                if (command.Type == "sidebar.motion.done")
                {
                    if (target == sidebar && command.Index is { } step) SidebarHost?.AnimationFinished(step);
                    return;
                }
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
                onCommand(command);
            }
        };
    }
}
