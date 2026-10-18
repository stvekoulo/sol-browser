using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;

namespace Browser.App;

public sealed partial class PageHost
{
    private string? _pip;
    private int _pipContext, _pipRequest, _pipEntering;
    private DispatcherTimer? _pipTimer;
    private bool _pipProbing;
    public string? PipTab => _pip;
    public event Action<string>? PipReturnRequested;

    // Une activation Sol ouvre la fenêtre vidéo du moteur, sans déplacer le lecteur ni modifier sa page.
    private const string OpenNativePip = """
        (async () => {
          if (!document.pictureInPictureEnabled) return false;
          const roots = [document];
          for (let i = 0; i < roots.length; i++)
            for (const e of roots[i].querySelectorAll('*')) if (e.shadowRoot) roots.push(e.shadowRoot);
          const area = v => { const r = v.getBoundingClientRect(); return r.width * r.height; };
          const video = roots.flatMap(r => [...r.querySelectorAll('video')])
            .filter(v => !v.paused && v.readyState > 2 && v.videoWidth > 0 && !v.disablePictureInPicture && area(v) > 0)
            .sort((a, b) => area(b) - area(a))[0];
          if (!video) return false;
          try {
            await video.requestPictureInPicture();
            return true;
          } catch { return false; }
        })()
        """;

    private static async Task<JsonElement?> NativePipAsync(CoreWebView2 core, int context, string expression, bool gesture = false)
    {
        try
        {
            var json = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", JsonSerializer.Serialize(new
            {
                expression, contextId = context, awaitPromise = true, returnByValue = true, userGesture = gesture,
            }));
            using var result = JsonDocument.Parse(json);
            return result.RootElement.GetProperty("result").TryGetProperty("value", out var value) ? value.Clone() : null;
        }
        catch (Exception e) when (e is InvalidOperationException or COMException or JsonException or ArgumentException)
        { return null; } // Document ou vue fermé pendant la demande.
    }

    public async Task<bool> TryEnterPipAsync(string id)
    {
        if (!settings.AutoPip || Core(id) is not { } core || _sims.ContainsKey(id) || _pip == id) return false;
        var request = ++_pipRequest;
        _pipEntering++;
        try
        {
            using var tree = JsonDocument.Parse(await core.CallDevToolsProtocolMethodAsync("Page.getFrameTree", "{}"));
            var frames = new List<JsonElement> { tree.RootElement.GetProperty("frameTree") };
            for (var i = 0; i < frames.Count; i++)
                if (frames[i].TryGetProperty("childFrames", out var children)) frames.AddRange(children.EnumerateArray());
            foreach (var frame in frames)
            {
                if (request != _pipRequest || _visible.Contains(id) || Core(id) != core) return false;
                using var world = JsonDocument.Parse(await core.CallDevToolsProtocolMethodAsync("Page.createIsolatedWorld",
                    JsonSerializer.Serialize(new { frameId = frame.GetProperty("frame").GetProperty("id").GetString(), worldName = "Sol PiP" })));
                var context = world.RootElement.GetProperty("executionContextId").GetInt32();
                if (await NativePipAsync(core, context, OpenNativePip, gesture: true) is not { ValueKind: JsonValueKind.True }) continue;
                if (request != _pipRequest || _visible.Contains(id) || Core(id) != core)
                {
                    await NativePipAsync(core, context, "document.pictureInPictureElement ? document.exitPictureInPicture() : null");
                    return false;
                }
                if (_pip is not null) ExitPip();
                (_pip, _pipContext) = (id, context);
                DebugLog.Write($"pip.enter {id}");
                _pipTimer ??= CreatePipTimer();
                _pipTimer.Start();
                RuntimeChanged?.Invoke();
                return true;
            }
        }
        catch (Exception e) when (e is InvalidOperationException or COMException or JsonException or ArgumentException)
        { DebugLog.Write($"pip.unavailable {id} {e.GetType().Name}"); }
        finally { _pipEntering--; }
        return false;
    }

    public async void ExitPip()
    {
        _pipRequest++;
        if (_pip is not { } id) return;
        var core = Core(id);
        var context = _pipContext;
        _pip = null;
        _pipTimer?.Stop();
        DebugLog.Write($"pip.exit {id}");
        if (core is not null)
            await NativePipAsync(core, context, "document.pictureInPictureElement ? document.exitPictureInPicture() : null");
        RuntimeChanged?.Invoke();
    }

    private DispatcherTimer CreatePipTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += async (_, _) =>
        {
            if (_pipProbing || _pipEntering > 0 || _pip is not { } id) return;
            var core = Core(id);
            if (core is null) { ExitPip(); return; }
            _pipProbing = true;
            JsonElement? state;
            try { state = await NativePipAsync(core, _pipContext, "!!document.pictureInPictureElement"); }
            finally { _pipProbing = false; }
            if (_pip != id) return;
            if (state is { ValueKind: JsonValueKind.True }) return;
            ExitPip();
            // WebView2 ne distingue pas la croix et « Retour à l'onglet » : rendre la page source dans les deux cas.
            if (state is not null) PipReturnRequested?.Invoke(id);
        };
        return timer;
    }
}
