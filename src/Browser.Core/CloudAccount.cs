using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Browser.Core;

public sealed class CloudException(string code) : Exception(code)
{
    public string Code { get; } = code; // signed-out, credentials, exists, weak, network, too-large, server
}

public sealed record CloudBackup(string Id, string Device, long Bytes, DateTimeOffset CreatedAt);

/// <summary>
/// Compte Sol et API de sauvegarde (Neon Auth et Functions). N'envoie que des fichiers déjà scellés par <see cref="Backup"/> :
/// le service ne voit jamais la clé de sauvegarde. La session est un cookie ; chaque appel en tire un jeton de 15 minutes.
/// </summary>
public sealed class CloudAccount(HttpClient http, string authUrl, string apiUrl)
{
    // Better Auth exige une origine approuvée : celle de l'interface de Sol, déclarée dans les domaines du service.
    private const string Origin = "https://ui.navigateur";

    /// <summary>Le cookie de session du compte.</summary>
    public async Task<string> SignInAsync(string email, string password, bool create, CancellationToken ct = default)
    {
        object body = create ? new { email, password, name = email.Split('@')[0] } : new { email, password };
        using var request = new HttpRequestMessage(HttpMethod.Post, authUrl + (create ? "/sign-up/email" : "/sign-in/email"))
        { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        request.Headers.TryAddWithoutValidation("Origin", Origin);
        using var response = await SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new CloudException(AuthError(response.StatusCode == System.Net.HttpStatusCode.Unauthorized, await response.Content.ReadAsStringAsync(ct)));
        var cookie = response.Headers.TryGetValues("Set-Cookie", out var values) ? string.Join("; ", values.Select(v => v.Split(';')[0])) : "";
        if (cookie.Length == 0) throw new CloudException("server");
        return cookie;
    }

    public static string AuthError(bool unauthorized, string body)
    {
        if (unauthorized || body.Contains("INVALID_EMAIL_OR_PASSWORD", StringComparison.Ordinal)) return "credentials";
        if (body.Contains("USER_ALREADY_EXISTS", StringComparison.Ordinal)) return "exists";
        if (body.Contains("PASSWORD_TOO_SHORT", StringComparison.Ordinal) || body.Contains("PASSWORD_TOO_LONG", StringComparison.Ordinal)) return "weak";
        if (body.Contains("INVALID_EMAIL", StringComparison.Ordinal)) return "credentials";
        return "server";
    }

    public async Task<IReadOnlyList<CloudBackup>> ListAsync(string session, CancellationToken ct = default)
    {
        using var response = await ApiAsync(session, HttpMethod.Get, "/backups", null, ct);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return doc.RootElement.GetProperty("backups").EnumerateArray().Select(b => new CloudBackup(
            b.GetProperty("id").GetString()!, b.GetProperty("device").GetString()!, b.GetProperty("bytes").GetInt64(), b.GetProperty("createdAt").GetDateTimeOffset())).ToList();
    }

    public async Task UploadAsync(string session, byte[] sealedFile, string device, CancellationToken ct = default)
    {
        using var content = new ByteArrayContent(sealedFile);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var _ = await ApiAsync(session, HttpMethod.Post, "/backups?device=" + Uri.EscapeDataString(device), content, ct);
    }

    public async Task<byte[]> DownloadAsync(string session, string id, CancellationToken ct = default)
    {
        using var response = await ApiAsync(session, HttpMethod.Get, "/backups/" + Uri.EscapeDataString(id), null, ct);
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    public async Task DeleteAsync(string session, string id, CancellationToken ct = default)
    {
        using var _ = await ApiAsync(session, HttpMethod.Delete, "/backups/" + Uri.EscapeDataString(id), null, ct);
    }

    private async Task<string> TokenAsync(string session, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, authUrl + "/token");
        request.Headers.TryAddWithoutValidation("Cookie", session);
        request.Headers.TryAddWithoutValidation("Origin", Origin);
        using var response = await SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new CloudException("signed-out");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return doc.RootElement.GetProperty("token").GetString() ?? throw new CloudException("signed-out");
    }

    private async Task<HttpResponseMessage> ApiAsync(string session, HttpMethod method, string path, HttpContent? content, CancellationToken ct)
    {
        var token = await TokenAsync(session, ct);
        using var request = new HttpRequestMessage(method, apiUrl + path) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await SendAsync(request, ct);
        if (response.IsSuccessStatusCode) return response;
        var status = response.StatusCode;
        response.Dispose();
        throw new CloudException(status switch
        {
            System.Net.HttpStatusCode.Unauthorized => "signed-out",
            System.Net.HttpStatusCode.RequestEntityTooLarge => "too-large",
            _ => "server",
        });
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try { return await http.SendAsync(request, ct); }
        catch (HttpRequestException) { throw new CloudException("network"); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new CloudException("network"); }
    }
}
