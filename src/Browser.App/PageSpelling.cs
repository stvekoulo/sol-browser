using System.Runtime.InteropServices;
using System.Text.Json;
using Browser.Core;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;

namespace Browser.App;

public sealed partial class PageHost
{
    private sealed class SpellingField
    {
        public required string Text;
        public required int Mode;
        public required WindowsSpelling.Error[] Local;
        public required Func<string, Task<string>> Run; // l'onglet, ou le cadre d'un autre site qui contient le champ
        public required bool InFrame;
        public bool Inline; // vue de Sol sans menu du moteur : les suggestions accompagnent les fautes
        public SpellingIssue[]? Online;
        public DateTime Changed = DateTime.UtcNow;
        public bool Checking, Checked, Repaint;
    }

    private static readonly HttpClient SpellingHttp = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly OnlineSpelling _online = new(SpellingHttp);
    private static readonly TimeSpan OnlinePause = TimeSpan.FromMilliseconds(1200); // pas de requête au milieu d'un mot
    private readonly WindowsSpelling _spelling = new();
    private readonly DispatcherTimer _spellingTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private bool _spellingStarted, _spellingBusy;
    private readonly Dictionary<string, SpellingField> _spellingCache = new();
    private readonly HashSet<string> _spellingSeen = new(); // onglets où le correcteur de Sol a été posé
    private readonly Dictionary<string, List<CoreWebView2Frame>> _spellingFrames = new();
    // Le script n'est envoyé qu'une fois par document ; les passages suivants ne font que l'interroger.
    private const string SpellingRead = "window.__solSpelling ? window.__solSpelling.read() : 'missing'";
    public string[] SpellingLanguages => _spelling.Languages;
    public string SpellingAccent { get; set; } = "#6495ed";
    public void StopSpelling() => _spellingTimer.Stop();
    /// <summary>Champs de Sol lui-même : le chat, et la barre latérale en local seulement (une adresse ne quitte pas la machine).</summary>
    public Func<IEnumerable<(string Id, CoreWebView2? Core, bool Online)>>? SpellingViews { get; set; }

    private void WireSpellingFrames(string id, CoreWebView2 core)
    {
        core.FrameCreated += (_, e) =>
        {
            var frames = _spellingFrames.TryGetValue(id, out var known) ? known : _spellingFrames[id] = [];
            frames.Add(e.Frame);
            e.Frame.Destroyed += (_, _) => frames.Remove(e.Frame);
        };
    }

    private void StartSpelling()
    {
        if (_spellingStarted) return;
        _spellingStarted = true;
        _spellingTimer.Tick += async (_, _) =>
        {
            if (_spellingBusy) return;
            _spellingBusy = true;
            try
            {
                var targets = _visible.Select(id => (Id: id, Core: Core(id), Online: Core(id) is { } page && OnlineAllowed(id, page), Inline: false))
                    .Concat((SpellingViews?.Invoke() ?? []).Select(v => (v.Id, v.Core, Online: v.Online && settings.SpellingOnline, Inline: true))).ToArray();
                foreach (var target in targets)
                {
                    if (target.Core is null) continue;
                    try { await CheckFieldAsync(target.Id, target.Core, target.Online, target.Inline); }
                    catch (Exception e) when (e is COMException or InvalidOperationException or JsonException or TimeoutException or ArgumentException) { _spellingCache.Remove(target.Id); }
                }
            }
            finally { _spellingBusy = false; }
        };
        _spellingTimer.Start();
    }

    private bool OnlineAllowed(string id, CoreWebView2 core) =>
        settings.SpellingOnline && !IsPrivate(id) && !TabSessions.IsSignIn(core.Source);

    private async Task CheckFieldAsync(string id, CoreWebView2 core, bool online, bool inline)
    {
        if (!core.Source.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return;
        var local = SpellingLanguages.Any(l => settings.Spelling == 3 || l.StartsWith(settings.Spelling == 1 ? "fr" : "en"));
        if (settings.Spelling == 0 || !local && !online)
        {
            // Correcteur coupé : la page est remise en état une fois, puis laissée tranquille.
            var field = _spellingCache.GetValueOrDefault(id);
            if (_spellingSeen.Remove(id) | _spellingCache.Remove(id)) await (field?.Run ?? Runner(core))("window.__solSpelling?.clear()");
            return;
        }
        _spellingSeen.Add(id);
        var (json, run, inFrame) = await ReadFieldAsync(id, core);
        if (!inline && !ReferenceEquals(Core(id), core)) return;
        using var snapshot = JsonDocument.Parse(json);
        var root = snapshot.RootElement;
        _spellingCache.TryGetValue(id, out var known);
        if (root.ValueKind != JsonValueKind.Object || root.TryGetProperty("frame", out _))
        {
            if (known is not null)
            {
                _spellingCache.Remove(id);
                if (known.InFrame) await known.Run("window.__solSpelling?.clear()");
            }
            return;
        }
        if (root.TryGetProperty("idle", out _) && known is not null) run = known.Run;
        else
        {
            if (!root.TryGetProperty("text", out var value) || value.ValueKind != JsonValueKind.String) return;
            var text = value.GetString() ?? "";
            if (text.Length > OnlineSpelling.MaxLength) return;
            if (known is null || known.Text != text || known.Mode != settings.Spelling)
                _spellingCache[id] = known = new SpellingField { Text = text, Mode = settings.Spelling, Local = inline && !text.Contains(' ') ? [] : _spelling.Check(text, settings.Spelling), Run = run, InFrame = inFrame, Inline = inline };
            known.Repaint = true;
        }
        if (online && !known.Checked && !known.Checking && DateTime.UtcNow - known.Changed >= OnlinePause && _online.Worth(known.Text))
            _ = CheckOnlineAsync(known);
        if (!known.Repaint) return;
        known.Repaint = false;
        await known.Run($"window.__solSpelling.paint({JsonSerializer.Serialize(known.Text)}, {JsonSerializer.Serialize(Merged(known))}, {JsonSerializer.Serialize(SpellingAccent)}, {(known.Inline ? "true" : "false")})");
    }

    private async Task CheckOnlineAsync(SpellingField field)
    {
        field.Checking = true;
        var issues = await _online.CheckAsync(field.Text, field.Mode);
        field.Checking = false;
        if (issues is null) return; // service absent ou quota atteint : la correction locale reste, nouvel essai au prochain passage
        field.Checked = true;
        // Un nom propre ou un mot d'une autre langue connu de Windows n'est pas une faute.
        var hasLocal = SpellingLanguages.Length > 0;
        field.Online = issues.Where(i => i.Kind != 0 || !hasLocal
            || SpellingFilter.Keep(field.Text, i.Start, i.Length) && _spelling.Check(field.Text.Substring(i.Start, i.Length), field.Mode).Length > 0).ToArray();
        field.Repaint = true;
    }

    private object[][] Merged(SpellingField field)
    {
        var online = field.Online ?? [];
        var marks = online.Select(i => (i.Start, i.Length, i.Kind, i.Suggestions))
            .Concat(field.Local.Where(e => !online.Any(o => o.Start < e.Start + e.Length && e.Start < o.Start + o.Length))
                .Select(e => (e.Start, e.Length, Kind: 0, Suggestions: field.Inline ? _spelling.Suggest(field.Text.Substring(e.Start, e.Length), field.Mode) : [])))
            .OrderBy(e => e.Start);
        return marks.Select(e => field.Inline ? new object[] { e.Start, e.Length, e.Kind, e.Suggestions } : [e.Start, e.Length, e.Kind]).ToArray();
    }

    private static Func<string, Task<string>> Runner(CoreWebView2 core) =>
        script => core.ExecuteScriptAsync(script).AsTask().WaitAsync(TimeSpan.FromSeconds(2));
    private static Func<string, Task<string>> Runner(CoreWebView2Frame frame) =>
        script => frame.ExecuteScriptAsync(script).AsTask().WaitAsync(TimeSpan.FromSeconds(2));

    private async Task<(string Json, Func<string, Task<string>> Run, bool InFrame)> ReadFieldAsync(string id, CoreWebView2 core)
    {
        var run = Runner(core);
        var json = await ReadOrInstallAsync(run);
        // Le champ est dans un cadre d'un autre site : la page ne peut pas le lire, le moteur interroge chaque cadre.
        if (!json.Contains("\"frame\"", StringComparison.Ordinal)) return (json, run, false);
        foreach (var frame in _spellingFrames.GetValueOrDefault(id)?.ToArray() ?? [])
        {
            var inner = Runner(frame);
            try
            {
                var value = await ReadOrInstallAsync(inner);
                if (value != "null") return (value, inner, true);
            }
            catch (Exception e) when (e is COMException or InvalidOperationException or TimeoutException) { /* cadre détruit ou bloqué */ }
        }
        return (json, run, false);
    }

    private static async Task<string> ReadOrInstallAsync(Func<string, Task<string>> run)
    {
        var json = await run(SpellingRead);
        if (json != "\"missing\"") return json;
        await run(SpellingScript);
        return await run(SpellingRead);
    }

    public async Task<(string Word, string[] Suggestions)> SpellingSuggestions(string id)
    {
        if (settings.Spelling == 0 || Core(id) is not { } core) return ("", []);
        try
        {
            var field = _spellingCache.GetValueOrDefault(id);
            using var doc = JsonDocument.Parse(await (field?.Run ?? Runner(core))("window.__solSpelling?.word() ?? null"));
            if (doc.RootElement.ValueKind != JsonValueKind.String) return ("", []);
            var word = doc.RootElement.GetString()!;
            if (field?.Online?.FirstOrDefault(i => i.Suggestions.Length > 0 && string.CompareOrdinal(field.Text, i.Start, word, 0, i.Length) == 0 && word.Length == i.Length) is { } issue)
                return (word, issue.Suggestions);
            return _spelling.Check(word, settings.Spelling).Length == 0 ? ("", []) : (word, _spelling.Suggest(word, settings.Spelling));
        }
        catch (Exception e) when (e is COMException or InvalidOperationException or JsonException or TimeoutException) { return ("", []); }
    }
    public async void CorrectSpelling(string id, string original, string replacement)
    {
        try
        {
            if (Core(id) is { } core)
                await (_spellingCache.GetValueOrDefault(id)?.Run ?? Runner(core))($"window.__solSpelling?.replace({JsonSerializer.Serialize(original)}, {JsonSerializer.Serialize(replacement)})");
        }
        catch (Exception e) when (e is COMException or InvalidOperationException or TimeoutException) { /* onglet fermé */ }
    }

    private static readonly string SpellingScript = File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "inject", "spelling.js"));
}
