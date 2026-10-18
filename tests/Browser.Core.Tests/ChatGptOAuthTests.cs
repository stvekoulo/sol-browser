using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Browser.Core;

namespace Browser.Core.Tests;

public sealed class ChatGptOAuthTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    [Fact]
    public void HostIdMigratesInvalidIdsAndRemainsStable()
    {
        var id = ChatGptOAuth.HostId("old-base64-host-id-that-was-not-an-accepted-urn");
        Assert.StartsWith("urn:uuid:", id);
        Assert.True(Guid.TryParse(id[9..], out _));
        Assert.Equal('4', id[23]);
        Assert.Equal(id, ChatGptOAuth.HostId(id));
    }

    [Fact]
    public void ReturningAuthorizationKeepsRegistrationAndIdentityHint()
    {
        var uri = new Uri(ChatGptOAuth.AuthorizeUrl("host", "http://127.0.0.1:5000/auth/callback", "s", "n", "c",
            new("oaiapp_saved", "sub1", "a@b.c"), "saved-id-token"));
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        Assert.Equal("oaiapp_saved", query["client_id"]);
        Assert.Null(query["agent_name_hint"]);
        Assert.Equal("saved-id-token", query["id_token_hint"]);
        Assert.Equal("a@b.c", query["login_hint"]);
        Assert.Equal("oaiapp_saved", ChatGptOAuth.CallbackClient(null, "oaiapp_saved"));
        Assert.Throws<FormatException>(() => ChatGptOAuth.CallbackClient("oaiapp_other", "oaiapp_saved"));
        Assert.Throws<FormatException>(() => ChatGptOAuth.CallbackClient(null, null));
        Assert.Throws<FormatException>(() => ChatGptOAuth.CallbackClient(ChatGptOAuth.DynamicClient, null));
    }

    private static (string Jwt, string Jwks) Sign(object claims, string kid = "k1")
    {
        using var rsa = RSA.Create(2048);
        var p = rsa.ExportParameters(false);
        var jwks = JsonSerializer.Serialize(new { keys = new[] { new { kty = "RSA", kid, n = ChatGptOAuth.Base64Url(p.Modulus!), e = ChatGptOAuth.Base64Url(p.Exponent!) } } });
        var head = ChatGptOAuth.Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", kid }));
        var body = ChatGptOAuth.Base64Url(JsonSerializer.SerializeToUtf8Bytes(claims));
        var sig = rsa.SignData(Encoding.ASCII.GetBytes(head + "." + body), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return (head + "." + body + "." + ChatGptOAuth.Base64Url(sig), jwks);
    }

    private static object Claims(string aud = "oaiapp_1", string nonce = "n1", long exp = 1_800_000_600) =>
        new { iss = ChatGptOAuth.Issuer, aud, sub = "user-1", email = "a@b.c", nonce, exp, iat = 1_800_000_000 };

    [Fact]
    public void VerifiesSignedIdToken()
    {
        var (jwt, jwks) = Sign(Claims());
        Assert.Equal(("user-1", "a@b.c"), ChatGptOAuth.VerifyIdToken(jwt, jwks, "oaiapp_1", "n1", Now));
    }

    [Theory]
    [InlineData("oaiapp_2", "n1", 1_800_000_600)] // autre client
    [InlineData("oaiapp_1", "n2", 1_800_000_600)] // nonce rejoué
    [InlineData("oaiapp_1", "n1", 1_799_999_000)] // expiré
    public void RejectsWrongClaims(string aud, string nonce, long exp)
    {
        var (jwt, jwks) = Sign(Claims(aud, nonce, exp));
        Assert.Throws<FormatException>(() => ChatGptOAuth.VerifyIdToken(jwt, jwks, "oaiapp_1", "n1", Now));
    }

    [Fact]
    public void RejectsForeignKey()
    {
        var (jwt, _) = Sign(Claims());
        var (_, other) = Sign(Claims());
        Assert.Throws<FormatException>(() => ChatGptOAuth.VerifyIdToken(jwt, other, "oaiapp_1", "n1", Now));
    }

    [Fact]
    public void ChallengeIsUnpaddedS256()
    {
        // Valeur calculée séparément (Node : createHash('sha256').digest('base64url')).
        Assert.Equal("WMo01PZ5DG9J7Igameq2B0-DTA3g-W_EYEFMedPH38A",ChatGptOAuth.Challenge("dBjftJeZ4CVP-mJ92K1z9VhzYCe_dP9d9C1v0xHyQ4E"));
    }

    [Fact]
    public void AuthorizeUrlRequestsPlanUsage()
    {
        var url = new Uri(ChatGptOAuth.AuthorizeUrl("host", "http://127.0.0.1:5000/auth/callback", "s", "n", "c"));
        var query = System.Web.HttpUtility.ParseQueryString(url.Query);
        Assert.Equal("dynamic_agent_client", query["client_id"]);
        Assert.Equal("http://127.0.0.1:5000/auth/callback", query["redirect_uri"]);
        Assert.True(ChatGptOAuth.HasPlan(query["scope"]!));
        Assert.Equal("S256", query["code_challenge_method"]);
    }

    [Fact]
    public void RefreshKeepsPreviousScopesWhenOmitted()
    {
        var tokens = ChatGptOAuth.ParseTokens("""{"access_token":"a","refresh_token":"r2","expires_in":3600,"token_type":"Bearer"}""", "openid chatgpt.tokens.use.direct", Now);
        Assert.Equal("r2", tokens.Refresh);
        Assert.Equal(Now.AddHours(1), tokens.Expires);
        Assert.True(ChatGptOAuth.HasPlan(tokens.Scopes));
        Assert.False(ChatGptOAuth.HasPlan("openid profile email"));
    }

    [Fact]
    public void ReadsStreamEvents()
    {
        Assert.Equal(("response.output_text.delta", "Bon", null), ChatGptOAuth.StreamEvent("""{"type":"response.output_text.delta","delta":"Bon"}"""));
        Assert.Null(ChatGptOAuth.StreamEvent("""{"type":"response.completed","response":{}}""").Error);
        Assert.Equal("subscription_sharing_usage_limit_exceeded",
            ChatGptOAuth.StreamEvent("""{"type":"response.failed","response":{"error":{"code":"subscription_sharing_usage_limit_exceeded"}}}""").Error);
        Assert.Equal("incomplete", ChatGptOAuth.StreamEvent("""{"type":"response.incomplete"}""").Error);
    }

    [Fact]
    public void PicksFirstListedModel()
    {
        Assert.Equal("b", ChatGptOAuth.FirstModel("""{"models":[{"slug":"a","visibility":"hide"},{"slug":"b","visibility":"list"},{"slug":"c","visibility":"list"}]}"""));
        Assert.Equal("http-429", ChatGptOAuth.ErrorCode("not json", 429));
        Assert.Equal("subscription_sharing_usage_unavailable", ChatGptOAuth.ErrorCode("""{"error":{"code":"subscription_sharing_usage_unavailable"}}""", 403));
    }
}
