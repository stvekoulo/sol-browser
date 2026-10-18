using Browser.Core;
using Microsoft.UI.Xaml;

namespace Browser.App;

public sealed partial class MainWindow
{
    // Même geste que la sidebar des onglets, en sens inverse : le chat est ancré à droite.
    private void WireChatGrip(ChatHost chat, Settings settings)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        void Save() => JsonFileStore.Save(AppPaths.Settings, settings);
        timer.Tick += (_, _) =>
        {
            if (!chat.IsOpen || ChatGrip.Visibility != Visibility.Visible)
            {
                timer.Stop(); Save(); return;
            }
            if (GetCursorPos(out var point) && ScreenToClient(_hwnd, ref point))
            {
                var area = ContentArea.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point(0, 0));
                chat.Resize(area.X + ContentArea.ActualWidth - point.X / Root.XamlRoot.RasterizationScale + 4);
            }
            // Appliquer aussi la dernière position au relâchement, même pour un geste très rapide.
            var button = GetSystemMetrics(23 /* SM_SWAPBUTTON */) != 0 ? 0x02 : 0x01;
            if ((GetAsyncKeyState(button) & 0x8000) == 0) { timer.Stop(); Save(); }
        };
        long lastPress = 0;
        ChatGrip.PointerPressed += (_, e) =>
        {
            var properties = e.GetCurrentPoint(ChatGrip).Properties;
            if (GetSystemMetrics(23) != 0 ? !properties.IsRightButtonPressed : !properties.IsLeftButtonPressed) return;
            e.Handled = true;
            var now = Environment.TickCount64;
            var doubleClick = now - lastPress <= GetDoubleClickTime();
            lastPress = doubleClick ? 0 : now;
            if (!doubleClick) { timer.Start(); return; }
            timer.Stop(); chat.Resize(ChatHost.DefaultWidth * settings.UiFactor); Save();
        };
        Closed += (_, _) => timer.Stop();
    }
}
