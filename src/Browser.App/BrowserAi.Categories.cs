using Browser.Core;
using Microsoft.UI.Xaml;
using System.Text;
using System.Text.Json;

namespace Browser.App;

// Classement quotidien des favoris et de l'historique, affiché sur la page d'accueil.
// Sol vérifie chaque heure si le dernier classement a plus de 24 h ; un seul classement à la fois par fenêtre.
public sealed partial class BrowserController
{
    private static string CategoriesFile => Path.Combine(AppPaths.DataDir, "ai-categories.json");
    private CategoryIndex _categories = AppPaths.Private ? new() : JsonFileStore.Load<CategoryIndex>(CategoriesFile);
    private readonly DispatcherTimer _categoriesTimer = new() { Interval = TimeSpan.FromHours(1) };
    private bool _categorizing;
    private CancellationTokenSource? _categoriesCancel;
    private void CancelCategories() { try { _categoriesCancel?.Cancel(); } catch (ObjectDisposedException) { } }
    private string? _categoriesError;

    private void StartAiCategories()
    {
        _categoriesTimer.Tick += (_, _) => _ = CategorizeAsync(false);
        _categoriesTimer.Start();
        // Laisser la fenêtre s'ouvrir avant le premier contrôle.
        _ = Task.Delay(TimeSpan.FromSeconds(30)).ContinueWith(_ => _window.DispatcherQueue.TryEnqueue(() => _ = CategorizeAsync(false)));
    }

    private async Task CategorizeAsync(bool force)
    {
        if (_closingWindow || _categorizing || AppPaths.Private || !_settings.SolAiCategories || !AiReady) return;
        // Verrou de fichier partagé entre fenêtres, libéré aussi si un processus se ferme brutalement.
        using var lease = CategoryLease();
        if (lease == null) return;
        // Une autre fenêtre du profil a peut-être déjà fait le classement du jour.
        _categories = JsonFileStore.Load<CategoryIndex>(CategoriesFile);
        if (!force && !SolAiCategories.Due(_categories, DateTimeOffset.UtcNow)) { Refresh(); return; }
        var links = SolAiCategories.Links(_db.Bookmarks(), _db.FrequentSites(150).Concat(_db.RecentHistory(150)));
        if (links.Count == 0) return;
        _categorizing = true; _categoriesError = null; PublishAiSettings();
        using var cancel = _categoriesCancel = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var answer = new StringBuilder();
        try
        {
            await _chatGpt.StreamAsync(SolAiCategories.Instruction(Language.Current), SolAiCategories.Data(links), delta => answer.Append(delta), cancel.Token);
            cancel.Token.ThrowIfCancellationRequested();
            var categories = SolAiCategories.Parse(answer.ToString(), links);
            if (categories.Length == 0) _categoriesError = "categories-empty";
            else if (_settings.SolAiCategories)
            {
                _categories = new() { UpdatedAt = DateTimeOffset.UtcNow, Categories = categories };
                JsonFileStore.Save(CategoriesFile, _categories);
            }
        }
        catch (ChatGptException e) { _categoriesError = e.Code; }
        catch (Exception e) when (e is HttpRequestException or IOException or JsonException or FormatException or OperationCanceledException)
        { DebugLog.Write("chatgpt.categories " + e.GetType().Name); _categoriesError = "network"; }
        _categoriesCancel = null; _categorizing = false;
        if (_closingWindow) return;
        PublishAiSettings(); Refresh();
    }

    private void SetAiCategories(bool on)
    {
        _settings.SolAiCategories = on; JsonFileStore.Save(AppPaths.Settings, _settings);
        _categoriesError = null;
        if (on) _ = CategorizeAsync(false);
        else
        {
            CancelCategories();
            _categories = new();
            try { File.Delete(CategoriesFile); File.Delete(CategoriesFile + ".bak"); } catch (IOException) { }
            Refresh();
        }
        PublishAiSettings();
    }

    private void PublishAiSettings() { if (_settingsOpen && !_closingWindow) _popup.Post(SettingsJson()); }
    private static FileStream? CategoryLease()
    {
        try { return new(CategoriesFile + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return null; }
    }

    private object StartCategories(bool isPrivate) => isPrivate || !_settings.SolAiCategories ? Array.Empty<object>()
        : _categories.Categories.Select(c => new { name = c.Name, links = c.Links.Take(8).Select(l => new { url = l.Url, title = l.Title, icon = StartIcon(l.Url) }) });
}
