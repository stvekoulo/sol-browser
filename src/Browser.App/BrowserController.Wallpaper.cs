using System.Text.Json;
using Browser.Core;

namespace Browser.App;

// Fond de la page d'accueil : l'image de Sol, une photo choisie, ou une nouvelle photo chaque jour.
public sealed partial class BrowserController
{
    // Une photo de 2560 pixels sur une connexion lente : plus de temps que les requêtes courtes du navigateur.
    private static readonly HttpClient WallpaperHttp = new()
    {
        Timeout = TimeSpan.FromSeconds(90),
        MaxResponseContentBufferSize = MaxWallpaper,
        DefaultRequestHeaders = { { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Sol" } },
    };
    private const int MaxWallpaper = 8_000_000;
    // Commun à tous les profils : une photo téléchargée ne l'est qu'une fois.
    private static string WallpaperFolder => Path.Combine(AppPaths.BaseDir, "wallpapers");
    private (int Id, string Data)? _wallpaperLoaded; // photo en mémoire, prête à être envoyée aux pages d'accueil
    private int _wallpaperRequest;

    private StartWallpaper? CurrentWallpaper => _settings.StartWallpaper && !_settings.StartWallpaperDaily && _settings.StartWallpaperCustom
        ? new(-1, "", "", 40, 68, 105) : StartWallpapers.Selected(_settings, DateOnly.FromDateTime(DateTime.Now));

    private void SetWallpaper(int photo)
    {
        if (photo != 0 && StartWallpapers.Find(photo) is null) return;
        _settings.StartWallpaperCustom = false;
        (_settings.StartWallpaperPhoto, _settings.StartWallpaperDaily) = (photo, false);
        WallpaperChanged();
    }

    private void SetWallpaperDaily(bool daily)
    {
        _settings.StartWallpaperDaily = daily;
        WallpaperChanged();
    }

    private void WallpaperChanged()
    {
        JsonFileStore.Save(AppPaths.Settings, _settings);
        Refresh(); // les pages d'accueil apprennent quelle image attendre
        _ = PostStartWallpaperAsync(null);
        if (_settingsOpen) _popup.Post(SettingsJson());
    }

    /// <summary>Envoie la photo aux pages d'accueil ouvertes (ou à une seule). Sans photo, ou si elle n'a pas pu être
    /// téléchargée, la page garde l'image livrée avec Sol.</summary>
    private async Task PostStartWallpaperAsync(string? tabId)
    {
        // Un nouveau choix remplace les envois en cours ; l'ouverture d'un onglet, non.
        var request = tabId is null ? ++_wallpaperRequest : _wallpaperRequest;
        var wanted = CurrentWallpaper;
        string? data = null;
        if (wanted is not null)
        {
            if (_wallpaperLoaded is var (id, loaded) && id == wanted.Id) data = loaded;
            else
            {
                data = await LoadWallpaperAsync(wanted);
                if (_closingWindow || request != _wallpaperRequest) return; // un autre choix a été fait entre-temps : son envoi suit
                if (data is not null) _wallpaperLoaded = (wanted.Id, data);
            }
        }
        var (r, g, b) = wanted is not null && data is not null ? wanted.Tint : ((byte)40, (byte)68, (byte)105);
        var tint = $"rgb({r}, {g}, {b})";
        if (UiBridge.WallpaperTint != tint) { UiBridge.WallpaperTint = tint; _bridge.BroadcastTheme(); _bridge.RefreshPageColor(); }
        var json = JsonSerializer.Serialize(new
        {
            @event = "start-wallpaper", url = data ?? "", tint = data is null ? null : tint,
            author = data is null || wanted?.Id == -1 ? null : wanted!.Author, page = data is null || wanted?.Id == -1 ? null : wanted!.Page,
        });
        foreach (var tab in _tabs.Tabs.Where(t => t.Url == Settings.DefaultHomePage && (tabId is null || t.Id == tabId) && !_pages.IsAsleep(t.Id)))
            _pages.PostStart(tab.Id, json);
    }

    // La photo en « data: » : lue dans le dossier commun, téléchargée la première fois. Null : indisponible.
    private static async Task<string?> LoadWallpaperAsync(StartWallpaper wallpaper)
    {
        var file = Path.Combine(WallpaperFolder, wallpaper.Id + ".jpg");
        try
        {
            if (wallpaper.Id == -1) return File.Exists(CustomWallpaperFile) ? "data:image/jpeg;base64," + Convert.ToBase64String(await File.ReadAllBytesAsync(CustomWallpaperFile)) : null;
            if (!File.Exists(file))
            {
                var bytes = await WallpaperHttp.GetByteArrayAsync(wallpaper.ImageUrl);
                // Une image JPEG d'une taille plausible, rien d'autre.
                if (bytes.Length is < 20_000 or > MaxWallpaper || bytes[0] != 0xFF || bytes[1] != 0xD8) return null;
                Directory.CreateDirectory(WallpaperFolder);
                var partial = file + "." + Guid.NewGuid().ToString("N") + ".part";
                await File.WriteAllBytesAsync(partial, bytes);
                File.Move(partial, file, overwrite: true);
            }
            return "data:image/jpeg;base64," + Convert.ToBase64String(await File.ReadAllBytesAsync(file));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            DebugLog.Write($"fond d'accueil {wallpaper.Id} indisponible : {e.Message}");
            return null;
        }
    }
}
