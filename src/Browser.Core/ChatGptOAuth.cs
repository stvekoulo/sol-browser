using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Browser.Core;

// Jetons d'un compte ChatGPT : un enregistrement par client délivré et identité vérifiée.
public sealed record ChatGptTokens(string ClientId, string Subject, string Email, string IdToken, string AccessToken,
    string RefreshToken, DateTimeOffset Expires, string Scopes);
public sealed record ChatGptRegistration(string ClientId, string? Subject, string Email);

// Parcours « Sign in with ChatGPT » pour les applications open source exécutées localement.
// https://developers.openai.com/siwc/token-sharing-open-source/sign-in
public static class ChatGptOAuth
{
    public const string Issuer = "https://auth.openai.com";
    public const string Resource = "https://api.openai.com/v1";
    public const string AuthorizeEndpoint = Issuer + "/api/accounts/authorize";
    public const string TokenEndpoint = Issuer + "/api/accounts/oauth/token";
    public const string Discovery = Issuer + "/.well-known/openid-configuration";
    public const string UsagePage = "https://chatgpt.com/settings/usage";
    public const string DynamicClient = "dynamic_agent_client";
    public const string PlanScope = "chatgpt.tokens.use.direct";
    private const string Scope = "openid profile email offline_access resource.invoke " + PlanScope;

    public static string RandomValue() => Base64Url(RandomNumberGenerator.GetBytes(32));
    public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    public static bool HasPlan(string scopes) => scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(PlanScope);
    public static string HostId(string? saved) => saved is not null && saved.StartsWith("urn:uuid:", StringComparison.Ordinal)
        && Guid.TryParseExact(saved[9..], "D", out var id) && id.ToString("D")[14] == '4' ? saved : "urn:uuid:" + Guid.NewGuid().ToString("D");
    public static string CallbackClient(string? returned, string? expected)
    {
        var client = returned ?? expected;
        if (string.IsNullOrWhiteSpace(client) || client == DynamicClient || expected != null && expected != client)
            throw new FormatException("invalid-client");
        return client;
    }

    // Premier enregistrement : client dynamique et nom affiché de l'application.
    public static string AuthorizeUrl(string hostId, string redirect, string state, string nonce, string challenge,
        ChatGptRegistration? registration = null, string? idToken = null)
    {
        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = registration?.ClientId ?? DynamicClient, ["ext_agent_host_id"] = hostId,
            ["response_type"] = "code", ["redirect_uri"] = redirect, ["scope"] = Scope, ["resource"] = Resource,
            ["state"] = state, ["nonce"] = nonce, ["code_challenge_method"] = "S256", ["code_challenge"] = challenge,
        };
        if (registration == null) parameters["agent_name_hint"] = "Sol";
        else
        {
            if (!string.IsNullOrEmpty(idToken)) parameters["id_token_hint"] = idToken;
            if (registration.Email.Length > 0) parameters["login_hint"] = registration.Email;
        }
        return AuthorizeEndpoint + "?" + string.Join("&", parameters.Select(p => p.Key + "=" + Uri.EscapeDataString(p.Value)));
    }

    // Réponse du point d'accès des jetons. Sans « scope » au renouvellement, l'autorisation précédente est conservée.
    public static (string Access, string Refresh, string? IdToken, DateTimeOffset Expires, string Scopes) ParseTokens(string json, string previousScopes, DateTimeOffset now)
    {
        using var data = JsonDocument.Parse(json);
        var root = data.RootElement;
        var access = root.GetProperty("access_token").GetString();
        var expires = root.GetProperty("expires_in").GetDouble();
        if (string.IsNullOrEmpty(access) || !double.IsFinite(expires) || expires <= 0 || expires >= (DateTimeOffset.MaxValue - now).TotalSeconds
            || !string.Equals(root.TryGetProperty("token_type", out var type) ? type.GetString() : "Bearer", "Bearer", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("invalid-token-response");
        var scopes = root.TryGetProperty("scope", out var scope) && scope.GetString() is { Length: > 0 } granted ? granted : previousScopes;
        var refresh = root.TryGetProperty("refresh_token", out var r) ? r.GetString() ?? "" : "";
        if (refresh.Length == 0) throw new FormatException("missing-refresh-token");
        return (access, refresh, root.TryGetProperty("id_token", out var id) ? id.GetString() : null, now.AddSeconds(expires), scopes);
    }

    // Jeton d'identité RS256 vérifié avec les clés publiées par OpenAI. Retourne (sub, email).
    public static (string Subject, string Email) VerifyIdToken(string jwt, string jwks, string clientId, string? nonce, DateTimeOffset now)
    {
        var parts = jwt.Split('.');
        if (parts.Length != 3) throw new FormatException("invalid-id-token");
        using var header = JsonDocument.Parse(FromBase64Url(parts[0]));
        if (header.RootElement.GetProperty("alg").GetString() != "RS256") throw new FormatException("invalid-id-token");
        var kid = header.RootElement.TryGetProperty("kid", out var k) ? k.GetString() : null;
        using var keys = JsonDocument.Parse(jwks);
        var key = keys.RootElement.GetProperty("keys").EnumerateArray()
            .FirstOrDefault(j => j.GetProperty("kty").GetString() == "RSA" && (kid == null || j.TryGetProperty("kid", out var id) && id.GetString() == kid));
        if (key.ValueKind != JsonValueKind.Object) throw new FormatException("unknown-signing-key");
        using var rsa = RSA.Create(new RSAParameters
        {
            Modulus = FromBase64Url(key.GetProperty("n").GetString()!), Exponent = FromBase64Url(key.GetProperty("e").GetString()!),
        });
        if (!rsa.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), FromBase64Url(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            throw new FormatException("invalid-id-token");
        using var payload = JsonDocument.Parse(FromBase64Url(parts[1]));
        var claims = payload.RootElement;
        var audience = claims.GetProperty("aud");
        var skew = TimeSpan.FromSeconds(5);
        var valid = claims.GetProperty("iss").GetString() == Issuer
            && (audience.ValueKind == JsonValueKind.String ? audience.GetString() == clientId : audience.EnumerateArray().Any(a => a.GetString() == clientId))
            && (!claims.TryGetProperty("azp", out var azp) || azp.GetString() == clientId)
            && DateTimeOffset.FromUnixTimeSeconds(claims.GetProperty("exp").GetInt64()) > now - skew
            && DateTimeOffset.FromUnixTimeSeconds(claims.GetProperty("iat").GetInt64()) < now + skew
            && (nonce == null || claims.TryGetProperty("nonce", out var n) && n.GetString() == nonce);
        var subject = claims.TryGetProperty("sub", out var sub) ? sub.GetString() : null;
        if (!valid || string.IsNullOrEmpty(subject)) throw new FormatException("invalid-id-token");
        return (subject, claims.TryGetProperty("email", out var email) ? email.GetString() ?? "" : "");
    }

    // Une ligne « data: » du flux de la Responses API : type, texte ajouté, code d'erreur.
    public static (string Type, string Delta, string? Error) StreamEvent(string json)
    {
        using var data = JsonDocument.Parse(json);
        var root = data.RootElement;
        var type = root.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
        var delta = type == "response.output_text.delta" && root.TryGetProperty("delta", out var d) ? d.GetString() ?? "" : "";
        string? error = type switch
        {
            "response.failed" => root.TryGetProperty("response", out var r) && r.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Object
                && e.TryGetProperty("code", out var c) ? c.GetString() ?? "unknown_error" : "unknown_error",
            "error" => root.TryGetProperty("code", out var c) && c.GetString() is { } code ? code
                : root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Object && e.TryGetProperty("code", out var ec) ? ec.GetString() ?? "unknown_error" : "unknown_error",
            "response.incomplete" => "incomplete",
            _ => null,
        };
        return (type, delta, error);
    }

    // Code d'une réponse HTTP en erreur ({ "error": { "code": … } }), sinon le statut.
    public static string ErrorCode(string body, int status)
    {
        try
        {
            using var data = JsonDocument.Parse(body);
            if (data.RootElement.TryGetProperty("error", out var e))
                return e.ValueKind == JsonValueKind.String ? e.GetString() ?? $"http-{status}"
                    : e.TryGetProperty("code", out var c) && c.GetString() is { Length: > 0 } code ? code : $"http-{status}";
        }
        catch (JsonException) { }
        return $"http-{status}";
    }

    // Premier modèle listé pour ce compte, dans l'ordre du serveur.
    public static string? FirstModel(string json) => Models(json).Select(m => m.Slug).FirstOrDefault();

    // Modèles proposés à ce compte, dans l'ordre du serveur, avec leur nom affiché.
    public static IReadOnlyList<(string Slug, string Name)> Models(string json)
    {
        using var data = JsonDocument.Parse(json);
        return data.RootElement.GetProperty("models").EnumerateArray()
            .Where(m => m.TryGetProperty("visibility", out var v) && v.GetString() == "list" && m.TryGetProperty("slug", out var s) && s.GetString() is { Length: > 0 })
            .Select(m => (m.GetProperty("slug").GetString()!, m.TryGetProperty("display_name", out var d) && d.GetString() is { Length: > 0 } name ? name : m.GetProperty("slug").GetString()!))
            .ToList();
    }

    public static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static byte[] FromBase64Url(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s + new string('=', (4 - s.Length % 4) % 4));
    }
}
