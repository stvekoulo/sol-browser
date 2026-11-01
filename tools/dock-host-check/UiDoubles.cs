namespace Microsoft.UI.Xaml
{
    public enum Visibility { Visible, Collapsed }
    public class FrameworkElement
    {
        public double ActualWidth { get; set; } = 800;
        public double ActualHeight { get; set; } = 600;
        public event EventHandler? SizeChanged;
        public void Resize() => SizeChanged?.Invoke(this, EventArgs.Empty);
    }
    public record Thickness(double Left = 0, double Right = 0, double Top = 0, double Bottom = 0);
}
namespace Microsoft.UI.Xaml.Controls
{
    public class WebView2 { }
    public class Grid
    {
        public object? Parent { get; set; }
        public Microsoft.UI.Xaml.Thickness Margin { get; } = new();
        public Microsoft.UI.Xaml.Visibility Visibility { get; set; } = Microsoft.UI.Xaml.Visibility.Collapsed;
        public double Width { get; set; }
        public double Height { get; set; }
        public void UpdateLayout() { }
    }
}
namespace Browser.App
{
    public class UiBridge
    {
        public record Options(double UiFactor = 0.92);
        public static Options? Settings { get; set; }
        public List<string> Messages { get; } = [];
        public void Post(string json) => Messages.Add(json);
    }
    public static class DebugLog { public static void Write(string message) { } }
    public static class CornerClip
    {
        public static void Set(Microsoft.UI.Xaml.Controls.WebView2 view, float radius) { }
    }
}
