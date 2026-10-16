using System.Runtime.InteropServices;
using Browser.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Browser.App;

public sealed partial class MainWindow
{
    // Gestes : boutons latéraux de la souris, et balayage horizontal à deux doigts du pavé tactile, pour page
    // précédente et page suivante. Le moteur ne reconnaît pas ce balayage dans ce type de vue : on le lit ici.
    private const double SwipeThreshold = 480;
    private double _swipe;
    private DateTime _swipeAt;
    private bool _swipeDone;
    private DispatcherTimer? _swipeIdle;

    // Pastille à flèche qui sort du bord pendant le balayage : elle avance avec le geste, et la page change
    // quand elle est entièrement sortie. Un simple élément XAML posé sur la page, sans effet sur la souris.
    private DateTime _swipeStart;
    private bool _swipeProbed;
    private int _swipeId;

    // Demande à la page si elle a utilisé ce balayage. Si oui, le geste est abandonné ; sinon, s'il est allé au bout,
    // la navigation a lieu.
    private async Task SettleSwipeAsync(int gesture, double sinceMs, string? navigate)
    {
        var used = _controller is not null && await _controller.PageUsedSidewaysAsync(sinceMs);
        if (gesture != _swipeId) return; // un autre geste a commencé entre-temps
        if (used) { _swipeDone = true; HideSwipeHint(committed: false); return; }
        if (navigate is null) return;
        _controller?.Shortcut(navigate);
        HideSwipeHint(committed: true);
    }

    private void ShowSwipeHint(bool back, double progress)
    {
        SwipeHint.HorizontalAlignment = back ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        SwipeGlyph.Glyph = back ? "" : ""; // chevron gauche, chevron droit
        var eased = 1 - Math.Pow(1 - progress, 3);     // la pastille sort vite puis ralentit
        SwipeShift.X = (back ? -1 : 1) * (64 - 84 * eased);
        SwipeHint.Opacity = Math.Min(1, progress * 1.6);
        SwipeScale.ScaleX = SwipeScale.ScaleY = 0.7 + 0.3 * eased;

        // Geste abandonné avant le seuil : la pastille rentre.
        _swipeIdle ??= CreateSwipeIdleTimer();
        _swipeIdle.Start();
    }

    private DispatcherTimer CreateSwipeIdleTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) =>
        {
            if ((DateTime.UtcNow - _swipeAt).TotalMilliseconds < 300) return;
            timer.Stop();
            if (!_swipeDone) HideSwipeHint(committed: false);
        };
        return timer;
    }

    private void HideSwipeHint(bool committed)
    {
        _swipeIdle?.Stop();
        var ease = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(committed ? 380 : 220);
        var story = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        void Animate(DependencyObject target, string property, double to, TimeSpan begin)
        {
            var animation = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation { To = to, Duration = duration, BeginTime = begin, EasingFunction = ease };
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animation, target);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animation, property);
            story.Children.Add(animation);
        }
        // Validé : la pastille grossit un instant avant de s'effacer. Abandonné : elle retourne dans le bord.
        var back = SwipeHint.HorizontalAlignment == HorizontalAlignment.Left;
        Animate(SwipeHint, "Opacity", 0, committed ? TimeSpan.FromMilliseconds(120) : TimeSpan.Zero);
        if (committed) { Animate(SwipeScale, "ScaleX", 1.25, TimeSpan.Zero); Animate(SwipeScale, "ScaleY", 1.25, TimeSpan.Zero); }
        else Animate(SwipeShift, "X", back ? -64 : 64, TimeSpan.Zero);
        // À la fin, les valeurs redeviennent modifiables directement pour le prochain geste.
        story.Completed += (_, _) => { story.Stop(); SwipeHint.Opacity = 0; };
        story.Begin();
    }

    private void WireGestures()
    {
        // handledEventsToo : les vues web marquent ces événements comme traités, on les observe quand même.
        Root.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, e) =>
        {
            var position = e.GetCurrentPoint(PopupFrame).Position;
            if (PopupFrame.Visibility == Visibility.Visible && (position.X < 0 || position.Y < 0 || position.X >= PopupFrame.ActualWidth || position.Y >= PopupFrame.ActualHeight))
                _controller?.DismissAiPopup();
            var p = e.GetCurrentPoint(Root).Properties;
            if (p.IsXButton1Pressed) _controller?.Shortcut("nav.back");
            else if (p.IsXButton2Pressed) _controller?.Shortcut("nav.forward");
        }), true);

        Root.AddHandler(UIElement.PointerWheelChangedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, e) =>
        {
            // Les gestes de navigation et de zoom ne concernent que les pages, jamais les panneaux de Sol.
            var point = e.GetCurrentPoint(Pages).Position;
            if (PopupFrame.Visibility == Visibility.Visible || CaptureFrame.Visibility == Visibility.Visible ||
                point.X < 0 || point.Y < 0 || point.X >= Pages.ActualWidth || point.Y >= Pages.ActualHeight) return;
            var p = e.GetCurrentPoint(Root).Properties;
            // Ctrl + molette : zoom de la page.
            if (!p.IsHorizontalMouseWheel && e.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Control))
            {
                _controller?.Shortcut(p.MouseWheelDelta > 0 ? "zoom.in" : "zoom.out");
                return;
            }
            if (!p.IsHorizontalMouseWheel) return;
            var now = DateTime.UtcNow;
            if ((now - _swipeAt).TotalMilliseconds > 350) { _swipe = 0; _swipeDone = false; _swipeProbed = false; _swipeStart = now; _swipeId++; } // nouveau geste
            _swipeAt = now;
            _swipe += p.MouseWheelDelta;
            if (_swipeDone) return;
            // Un balayage dont la page se sert elle-même (carrousel, zone qui défile de côté) n'est pas un retour en
            // arrière : la page est consultée une première fois dès que le geste se dessine, puis avant de naviguer.
            // Tout le geste est alors écarté, même si le carrousel arrive en bout de course en chemin.
            // ponytail: seuil fixe ; un carrousel placé dans un cadre d'un autre site n'est pas vu.
            var progress = Math.Min(1, Math.Abs(_swipe) / SwipeThreshold);
            var since = (now - _swipeStart).TotalMilliseconds + 600;
            if (progress >= 0.2 && !_swipeProbed)
            {
                _swipeProbed = true;
                _ = SettleSwipeAsync(_swipeId, since, navigate: null);
            }
            ShowSwipeHint(back: _swipe < 0, progress);
            if (progress < 1) return;
            _swipeDone = true;
            _ = SettleSwipeAsync(_swipeId, since, navigate: _swipe < 0 ? "nav.back" : "nav.forward");
        }), true);
    }

}
