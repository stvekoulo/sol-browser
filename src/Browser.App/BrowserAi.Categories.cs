using Browser.Core;
using Microsoft.UI.Xaml;
using System.Text;
using System.Text.Json;

namespace Browser.App;

// Classement des favoris et de l'historique par site, affiché sur la page d'accueil. Sol vérifie chaque heure s'il
// reste des sites inconnus et en classe au plus BatchesPerDay lots par jour : une grande bibliothèque est traitée
// sur quelques jours, puis seuls les nouveaux sites partent chez OpenAI.
public sealed partial class BrowserController
{
    private static string CategoriesFile => Path.Combine(AppPaths.DataDir, "ai-categories.json");
    private CategoryIndex _categories = AppPaths.Private ? new() : JsonFileStore.Load<CategoryIndex>(CategoriesFile);
    private readonly DispatcherTimer _categoriesTimer = new() { Interval = TimeSpan.FromHours(1) };
    private bool _categorizing;
    private CancellationTokenSource? _categoriesCancel;
    private void CancelCategories() { try { _categoriesCancel?.Cancel(); } catch (ObjectDisposedException) { } }
    private string? _categoriesError;
    private (int Classified, int Pending, int Budget) _categoriesProgress;
    private (IReadOnlyList<CategoryView> Items, DateTime At)? _categoryView;

    private void StartAiCategories()
    {
        _categoriesTimer.Tick += (_, _) => _ = CategorizeAsync(false);
        _categoriesTimer.Start();
        // Laisser la fenêtre s'ouvrir avant le premier contrôle.
        _ = Task.Delay(TimeSpan.FromSeconds(30)).ContinueWith(_ => _window.DispatcherQueue.TryEnqueue(() => _ = CategorizeAsync(false)));
    }

    private IReadOnlyList<CategoryLink> CategoryLinks() => SolAiCategories.Links(_db.Bookmarks(), _db.FrequentSites(50_000));

    // force : « Classer maintenant » ajoute une série de lots même si celle du jour est déjà faite.
    private async Task CategorizeAsync(bool force)
    {
        if (_closingWindow || _categorizing || AppPaths.Private || !_settings.SolAiCategories || !AiReady) return;
        // Verrou de fichier partagé entre fenêtres, libéré aussi si un processus se ferme brutalement.
        using var lease = CategoryLease();
        if (lease == null) return;
        _categories = JsonFileStore.Load<CategoryIndex>(CategoriesFile);
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        if (_categories.Day != today) { _categories.Day = today; _categories.BatchesToday = 0; }
        var sites = SolAiCategories.Sites(CategoryLinks());
        var pending = SolAiCategories.Pending(_categories, sites).ToList();
        var batches = force ? SolAiCategories.BatchesPerDay : SolAiCategories.Budget(_categories, today);
        UpdateCategoryProgress(sites.Count, pending.Count, today);
        if (pending.Count == 0 || batches == 0) { PublishAiSettings(); return; }
        _categorizing = true; _categoriesError = null; PublishAiSettings();
        using var cancel = _categoriesCancel = new CancellationTokenSource();
        try
        {
            for (var round = 0; round < batches && pending.Count > 0 && !cancel.IsCancellationRequested && _settings.SolAiCategories; round++)
            {
                var batch = pending.Take(SolAiCategories.BatchSize).ToList();
                var answer = new StringBuilder();
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel.Token);
                deadline.CancelAfter(TimeSpan.FromMinutes(2));
                await _chatGpt.StreamAsync(SolAiCategories.Instruction(Language.Current, _categories.Names), SolAiCategories.Data(batch), delta => answer.Append(delta), deadline.Token);
                if (!SolAiCategories.Apply(_categories, answer.ToString(), batch, DateTimeOffset.UtcNow)) { _categoriesError = "categories-empty"; break; }
                _categories.BatchesToday++;
                if (!_settings.SolAiCategories) break;
                JsonFileStore.Save(CategoriesFile, _categories);
                pending.RemoveRange(0, batch.Count);
                _categoryView = null;
                UpdateCategoryProgress(sites.Count, pending.Count, today);
                PublishAiSettings(); Refresh();
            }
        }
        catch (ChatGptException e) { _categoriesError = e.Code; }
        catch (Exception e) when (e is HttpRequestException or IOException or JsonException or FormatException or OperationCanceledException)
        { DebugLog.Write("chatgpt.categories " + e.GetType().Name); if (!cancel.IsCancellationRequested) _categoriesError = "network"; }
        _categoriesCancel = null; _categorizing = false;
        if (_closingWindow) return;
        PublishAiSettings(); Refresh();
    }

    private void UpdateCategoryProgress(int sites, int pending, string today) =>
        _categoriesProgress = (sites - pending, pending, SolAiCategories.Budget(_categories, today));

    private void SetAiCategories(bool on)
    {
        _settings.SolAiCategories = on; JsonFileStore.Save(AppPaths.Settings, _settings);
        _categoriesError = null;
        if (on) _ = CategorizeAsync(false);
        else
        {
            CancelCategories();
            _categories = new(); _categoryView = null; _categoriesProgress = default;
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

    // Vue par catégorie, recalculée après un lot ou au plus toutes les 5 minutes : la page d'accueil est publiée souvent.
    private IReadOnlyList<CategoryView> CategoryViews()
    {
        if (_categories.Names.Length == 0) return [];
        if (_categoryView is { } cached && DateTime.UtcNow - cached.At < TimeSpan.FromMinutes(5)) return cached.Items;
        var items = SolAiCategories.View(_categories, CategoryLinks());
        _categoryView = (items, DateTime.UtcNow);
        return items;
    }

    private object StartCategories(bool isPrivate) => isPrivate || !_settings.SolAiCategories ? Array.Empty<object>()
        : CategoryViews().Select(c => new { name = c.Name, total = c.Total, links = c.Links.Select(l => new { url = l.Url, title = l.Title.Length > 0 ? l.Title : SolAiCategories.Domain(l.Url), icon = StartIcon(l.Url) }) });
}
