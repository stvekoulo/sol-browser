using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Browser.Core;
using Microsoft.Web.WebView2.Core;

namespace Browser.App;

public sealed partial class PageHost
{
    private sealed record GitLabCookie(string Name, string Value, string Domain, string Path, bool Secure, bool HttpOnly, CoreWebView2CookieSameSiteKind SameSite);
    private readonly HashSet<string> _gitLabRestored = new();
    private static string GitLabFile(string profile) => Path.Combine(AppPaths.DataDir, "gitlab-sessions", profile + ".bin");
    private static byte[] GitLabEntropy(string profile) => Encoding.UTF8.GetBytes(AppPaths.Profile + "/" + profile);

    private void RestoreGitLabSession(CoreWebView2Profile profile)
    {
        if (AppPaths.Private || !_gitLabRestored.Add(profile.ProfileName)) return;
        var path = GitLabFile(profile.ProfileName);
        if (!File.Exists(path)) return;
        try
        {
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(path), GitLabEntropy(profile.ProfileName), DataProtectionScope.CurrentUser);
            var cookies = JsonSerializer.Deserialize<GitLabCookie[]>(plain) ?? [];
            // Consommer la sauvegarde avant navigation : une déconnexion ne doit jamais la ressusciter.
            File.Delete(path);
            foreach (var saved in cookies)
            {
                var cookie = profile.CookieManager.CreateCookie(saved.Name, saved.Value, saved.Domain, saved.Path);
                cookie.Expires = -1;
                cookie.IsSecure = saved.Secure;
                cookie.IsHttpOnly = saved.HttpOnly;
                cookie.SameSite = saved.SameSite;
                profile.CookieManager.AddOrUpdateCookie(cookie);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException or JsonException or InvalidOperationException or COMException)
        { DebugLog.Write($"gitlab.session.restore.failed {error.GetType().Name}"); }
    }

    private static void SaveGitLabSession(string profile, IEnumerable<CoreWebView2Cookie> cookies)
    {
        var path = GitLabFile(profile);
        try
        {
            var saved = cookies.Where(c => c.IsSession && c.Name is "_gitlab_session" or "gitlab_user")
                .Select(c => new GitLabCookie(c.Name, c.Value, c.Domain, c.Path, c.IsSecure, c.IsHttpOnly, c.SameSite)).ToArray();
            if (saved.Length == 0) { if (File.Exists(path)) File.Delete(path); return; }
            var encrypted = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(saved), GitLabEntropy(profile), DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path + ".tmp", encrypted);
            File.Move(path + ".tmp", path, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException or InvalidOperationException or COMException)
        { DebugLog.Write($"gitlab.session.save.failed {error.GetType().Name}"); }
    }

    public async Task SaveGitLabSessionsAsync()
    {
        if (AppPaths.Private) return;
        await Task.WhenAll(_closingProfiles.Values.ToArray());
        foreach (var (name, cookies) in _sessionCookies.ToArray()) SaveGitLabSession(name, cookies);
        foreach (var profile in Profiles.ToArray())
        {
            try { SaveGitLabSession(profile.ProfileName, await profile.CookieManager.GetCookiesAsync("")); }
            catch (Exception error) when (error is InvalidOperationException or COMException)
            { DebugLog.Write($"gitlab.session.read.failed {error.GetType().Name}"); }
        }
    }
}
