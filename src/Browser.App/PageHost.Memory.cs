using Browser.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Browser.App;

public sealed partial class PageHost
{
    // Économie de mémoire. Un onglet masqué est mis en veille (scripts à l'arrêt, page gardée telle quelle) ;
    // s'il reste masqué longtemps, sa vue
    // est fermée et il redevient un onglet endormi, rechargé quand on y revient. Un onglet qui joue du son, ou que le
    // navigateur garde affiché ailleurs (vue scindée, appareil simulé), n'est jamais touché.
    // Diagnostic : NAVIGATEUR_SAVER_TEST=1 ramène les délais à quelques secondes pour pouvoir observer le mécanisme.
    private static readonly bool SaverTest = Environment.GetEnvironmentVariable("NAVIGATEUR_SAVER_TEST") == "1";
    private static readonly (TimeSpan SuspendAfter, TimeSpan DiscardAfter) Policy = TabMemoryPolicy.ForMemory(SystemMemory.Read().Total);
    private static readonly TimeSpan SuspendAfter = SaverTest ? TimeSpan.FromSeconds(4) : Policy.SuspendAfter;
    private static readonly TimeSpan DiscardAfter = SaverTest ? TimeSpan.FromSeconds(18) : Policy.DiscardAfter;
    public int DiscardMinutes => (int)Policy.DiscardAfter.TotalMinutes;
    private readonly Dictionary<string, DateTime> _hiddenSince = new();
    private readonly HashSet<string> _discardProtected = new();
    private readonly HashSet<string> _suspendRefused = new();
    private DispatcherTimer? _saver;

    /// <summary>Onglets à ne jamais décharger (donné par le navigateur : vue scindée…).</summary>
    public Func<string, bool> KeepAwake { get; set; } = _ => false;
    public event Action<string>? Discarded; // id : la vue a été fermée, l'onglet se rechargera
    public int AsleepCount => _asleep.Count;

    private void Rest(string id, WebView2 view, bool hidden)
    {
        if (view.CoreWebView2 is not { } core) return;
        try
        {
            if (hidden && _hiddenSince.TryAdd(id, DateTime.UtcNow) && settings.MemorySaver)
                core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
            else if (!hidden && _hiddenSince.Remove(id))
            {
                _discardProtected.Remove(id);
                Wake(id, core);
            }
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException) { /* vue fermée entre-temps */ }
        if (_saver is null)
        {
            _saver = new DispatcherTimer { Interval = SaverTest ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(15) };
            _saver.Tick += (_, _) => SaveMemory(DateTime.UtcNow);
            _saver.Start();
        }
    }

    /// <summary>Passe en revue les onglets masqués ; <paramref name="now"/> est donné pour pouvoir avancer l'horloge en test.</summary>
    public void SaveMemory(DateTime now)
    {
        if (!settings.MemorySaver) return;
        foreach (var (id, since) in _hiddenSince.ToList())
        {
            if (!_views.TryGetValue(id, out var view) || view.CoreWebView2 is not { } core) { _hiddenSince.Remove(id); continue; }
            try
            {
                if (core.IsDocumentPlayingAudio) { _hiddenSince[id] = now; continue; } // musique, appel : il reste éveillé
                var busy = KeepAwake(id) || _pip == id || _sims.ContainsKey(id) || _loading.Contains(id);
                if (busy) { _hiddenSince[id] = now; continue; }
                if (!IsPrivate(id) && !_discardProtected.Contains(id) && now - since >= DiscardAfter && core.Source.StartsWith("http", StringComparison.Ordinal) && !_errorUrl.ContainsKey(id)) _ = DiscardIfSafeAsync(id, core);
                else if (now - since >= SuspendAfter && !core.IsSuspended && !_suspendRefused.Contains(id)) _ = SuspendAsync(id, core);
            }
            catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException) { /* vue fermée entre-temps */ }
        }
    }

    private readonly HashSet<string> _saving = new();
    private async Task SuspendAsync(string id, CoreWebView2 core)
    {
        if (!_saving.Add(id)) return;
        try
        {
            await RestCoreAsync(id, core);
            if (!settings.MemorySaver || !_hiddenSince.ContainsKey(id) || KeepAwake(id)
                || _pip == id || _sims.ContainsKey(id) || _loading.Contains(id) || core.IsDocumentPlayingAudio) Wake(id, core);
        }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException)
        { DebugLog.Write($"memory.suspend.failed {id} {error.HResult}"); }
        finally { _saving.Remove(id); }
    }

    private async Task RestCoreAsync(string id, CoreWebView2 core)
    {
        core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
        if (await core.TrySuspendAsync()) DebugLog.Write($"memory.suspend {id} True");
        else
        {
            // Le moteur peut garder une activité vivante : réduire la mémoire sans bloquer ses scripts.
            core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
            _suspendRefused.Add(id);
            DebugLog.Write($"memory.low {id}");
        }
    }

    private void Wake(string id, CoreWebView2 core)
    {
        _suspendRefused.Remove(id);
        if (core.IsSuspended) core.Resume();
        else if (core.MemoryUsageTargetLevel != CoreWebView2MemoryUsageTargetLevel.Normal)
            core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
    }

    private async Task DiscardIfSafeAsync(string id, CoreWebView2 core)
    {
        if (!_saving.Add(id)) return;
        try
        {
            // Keep live state and embedded sign-in/media frames; suspension retains their browser context.
            if (TabSessions.IsSignIn(core.Source) || _previews.ContainsKey(id)) return;
            var source = core.Source;
            Wake(id, core);
            var safe = await core.ExecuteScriptAsync(@"(() => { try {
                return sessionStorage.length === 0 && !document.querySelector('iframe,[contenteditable]:not([contenteditable=false]),[role=textbox]')
                    && ![...document.querySelectorAll('input,textarea')].some(e => e.value !== e.defaultValue || ('checked' in e && e.checked !== e.defaultChecked))
                    && ![...document.querySelectorAll('select option')].some(e => e.selected !== e.defaultSelected)
                    && ![...document.querySelectorAll('video,audio')].some(e => !e.paused && !e.ended);
                } catch { return false; } })()");
            if (safe == "true" && settings.MemorySaver && _hiddenSince.ContainsKey(id)
                && CoreOf(id) == core && core.Source == source && !_loading.Contains(id) && !KeepAwake(id)
                && _pip != id && !_sims.ContainsKey(id) && !_previews.ContainsKey(id) && !core.IsDocumentPlayingAudio) Discard(id, core);
            else if (settings.MemorySaver && _hiddenSince.ContainsKey(id) && CoreOf(id) == core)
            {
                if (KeepAwake(id) || _pip == id || _sims.ContainsKey(id) || _loading.Contains(id) || core.IsDocumentPlayingAudio) return;
                _discardProtected.Add(id); // Un brouillon reste endormi sans être réveillé à chaque passage.
                await RestCoreAsync(id, core);
                if (!settings.MemorySaver || !_hiddenSince.ContainsKey(id) || KeepAwake(id)
                    || _pip == id || _sims.ContainsKey(id) || _loading.Contains(id) || core.IsDocumentPlayingAudio) Wake(id, core);
            }
        }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
        finally { _saving.Remove(id); }
    }

    private void Discard(string id, CoreWebView2 core)
    {
        DebugLog.Write($"memory.discard {id}");
        var name = core.Profile.ProfileName;
        var tab = new Tab(id, core.Source, core.DocumentTitle, SessionId: name.StartsWith("sol-") ? name[4..] : "");
        Remove(id);
        _hiddenSince.Remove(id);
        _asleep[id] = tab;
        Discarded?.Invoke(id);
    }

    // Zoom des pages : celui du moteur, appliqué par l'extension de Sol (voir PageZoom). Le réglage du profil vaut pour
    // tous les onglets ; la page de démarrage, les documents, les appareils simulés et le lecteur flottant restent à 100 %.
    public void SetZoom(double zoom)
    {
        settings.PageZoom = zoom;
        PageZoom.Set(zoom);
        RuntimeChanged?.Invoke();
        // Une page affichée relaie la demande : tous les onglets prennent le nouveau zoom tout de suite.
        foreach (var core in _views.Values.Select(v => v.CoreWebView2).OfType<CoreWebView2>().DistinctBy(c => c.Profile.ProfileName))
            _ = PageZoom.RefreshAsync(core);
    }

    // À la sortie d'un appareil simulé : la page retrouve les métriques de sa vue et le zoom du profil.
    private async Task ApplyZoomAsync(string id)
    {
        if (!_views.TryGetValue(id, out var view) || view.CoreWebView2 is not { } core || _mode.ContainsKey(id)) return;
        try { await core.CallDevToolsProtocolMethodAsync("Emulation.clearDeviceMetricsOverride", "{}"); }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException) { return; /* vue fermée entre-temps */ }
        await PageZoom.TabAsync(core, null);
    }

    public CoreWebView2? CoreOf(string id) => Core(id);


    private CoreWebView2? Core(string id) =>
        _views.TryGetValue(id, out var view) ? view.CoreWebView2 : null;
}
