using Browser.Core;
using Microsoft.Web.WebView2.Core;

namespace Browser.App;

// Nouvelle session d'onglets : elle part avec les connexions de l'onglet d'où elle est créée (comptes déjà ouverts,
// sélecteur de comptes du site), puis vit seule. Se connecter à un autre compte dans la session ne touche pas aux autres.
public sealed partial class PageHost
{
    private sealed record SeedCookie(string Name, string Value, string Domain, string Path, double Expires, bool HttpOnly, bool Secure,
        CoreWebView2CookieSameSiteKind SameSite, bool Session);
    private readonly Dictionary<string, SeedCookie[]> _sessionSeeds = new();

    public async Task SeedSessionAsync(string fromTabId, string sessionId)
    {
        if (!TabSessions.ValidId(sessionId) || IsPrivate(fromTabId) || Core(fromTabId) is not { } core) return;
        try
        {
            var cookies = await core.Profile.CookieManager.GetCookiesAsync("").AsTask().WaitAsync(TimeSpan.FromSeconds(4));
            _sessionSeeds["sol-" + sessionId] = cookies.Select(c => new SeedCookie(c.Name, c.Value, c.Domain, c.Path, c.Expires, c.IsHttpOnly, c.IsSecure, c.SameSite, c.IsSession)).ToArray();
        }
        catch (Exception e) when (e is TimeoutException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        { DebugLog.Write($"session.seed.failed {e.GetType().Name} {e.HResult}"); }
    }

    private void ApplySessionSeed(CoreWebView2Profile profile)
    {
        if (!_sessionSeeds.Remove(profile.ProfileName, out var seed)) return;
        var manager = profile.CookieManager;
        foreach (var s in seed)
            try
            {
                var cookie = manager.CreateCookie(s.Name, s.Value, s.Domain, s.Path);
                cookie.IsHttpOnly = s.HttpOnly; cookie.IsSecure = s.Secure; cookie.SameSite = s.SameSite;
                if (!s.Session) cookie.Expires = s.Expires;
                manager.AddOrUpdateCookie(cookie);
            }
            catch (Exception e) when (e is ArgumentException or System.Runtime.InteropServices.COMException) { }
    }
}
