using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System.Text;

namespace Browser.App;

// Un envoi à l'interface au plus toutes les 50 ms, même si le serveur produit beaucoup de petits fragments.
internal sealed class AiStreamBuffer(DispatcherQueue queue, Action<string> publish)
{
    private readonly object _gate = new();
    private readonly StringBuilder _pending = new();
    private DispatcherTimer? _timer;
    private bool _scheduled;

    public void Append(string delta)
    {
        lock (_gate)
        {
            _pending.Append(delta);
            if (_scheduled) return;
            _scheduled = true;
        }
        queue.TryEnqueue(() =>
        {
            if (_timer == null)
            {
                _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
                _timer.Tick += (_, _) => Flush();
            }
            _timer.Start();
        });
    }

    public void Flush()
    {
        _timer?.Stop();
        string text;
        lock (_gate) { text = _pending.ToString(); _pending.Clear(); _scheduled = false; }
        if (text.Length > 0) publish(text);
    }
}
