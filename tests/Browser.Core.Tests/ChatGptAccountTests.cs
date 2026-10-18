using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Browser.App;
using Browser.Core;

namespace Browser.Core.Tests;

// Exécute le vrai stockage Windows et le client HTTP, avec un serveur simulé sans accès réseau.
public sealed class ChatGptAccountTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sol-ai-test-" + Guid.NewGuid());
    private string TokenFile => Path.Combine(_directory, "chatgpt.bin");
    private static ChatGptTokens Tokens(bool expired = false) => new("oaiapp_test", "test-sub", "test@example.invalid", "verified-before-test",
        "old-access", "old-refresh", DateTimeOffset.UtcNow.AddHours(expired ? -1 : 1), ChatGptOAuth.PlanScope);

    public ChatGptAccountTests() => Directory.CreateDirectory(_directory);
    private void Save(ChatGptTokens tokens) => File.WriteAllBytes(TokenFile, ProtectedData.Protect(
        JsonSerializer.SerializeToUtf8Bytes(tokens), "Sol.ChatGPT"u8.ToArray(), DataProtectionScope.CurrentUser));
    private ChatGptTokens Load() => JsonSerializer.Deserialize<ChatGptTokens>(ProtectedData.Unprotect(
        File.ReadAllBytes(TokenFile), "Sol.ChatGPT"u8.ToArray(), DataProtectionScope.CurrentUser))!;
    public void Dispose() => Directory.Delete(_directory, true);

    private sealed class Server(Func<HttpRequestMessage, string> reply) : HttpMessageHandler
    {
        public readonly List<string> Paths = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply(request)) });
        }
    }
    private static string Discovery() => JsonSerializer.Serialize(new { issuer = ChatGptOAuth.Issuer,
        jwks_uri = ChatGptOAuth.Issuer + "/keys", revocation_endpoint = ChatGptOAuth.Issuer + "/revoke" });

    [Fact]
    public async Task RevokedPermissionIsRecheckedFromDiskBeforeInference()
    {
        Save(Tokens());
        using var server = new Server(_ => throw new Exception("HTTP must not be called"));
        using var http = new HttpClient(server);
        var account = new ChatGptAccount(_directory, http);
        Assert.True(account.Ready);
        Save(Tokens() with { Scopes = "openid profile" });
        var error = await Assert.ThrowsAsync<ChatGptException>(() => account.StreamAsync("instruction", "question", _ => { }, default));
        Assert.Equal("plan-not-granted", error.Code);
        Assert.False(account.Ready);
        Assert.Empty(server.Paths);
    }

    [Fact]
    public async Task InvalidRefreshedIdentityIsNeverSavedAsUsableAccess()
    {
        Save(Tokens(expired: true));
        using var server = new Server(r => r.RequestUri!.AbsolutePath switch
        {
            "/api/accounts/oauth/token" => """{"access_token":"unverified-access","refresh_token":"rotated-refresh","id_token":"broken","expires_in":3600}""",
            "/.well-known/openid-configuration" => Discovery(),
            "/keys" => """{"keys":[]}""",
            _ => throw new Exception("Inference must not be called"),
        });
        using var http = new HttpClient(server);
        var account = new ChatGptAccount(_directory, http);
        var error = await Assert.ThrowsAsync<ChatGptException>(() => account.StreamAsync("instruction", "question", _ => { }, default));
        Assert.Equal("invalid-id-token", error.Code);
        Assert.False(File.Exists(TokenFile));
        Assert.False(account.Ready);
        Assert.DoesNotContain("/v1/responses", server.Paths);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RefreshRotatesCredentialsAndOnlyCompletedStreamSucceeds(bool completed)
    {
        Save(Tokens(expired: true));
        using var server = new Server(r => r.RequestUri!.AbsolutePath switch
        {
            "/api/accounts/oauth/token" => """{"access_token":"new-access","refresh_token":"rotated-refresh","expires_in":3600}""",
            "/v1/models" => """{"models":[{"slug":"test-model","visibility":"list"}]}""",
            "/v1/responses" => Response(r, completed),
            _ => throw new Exception("Unexpected request"),
        });
        using var http = new HttpClient(server);
        var account = new ChatGptAccount(_directory, http);
        var text = new StringBuilder();
        var run = () => account.StreamAsync("instruction", "question", d => text.Append(d), default);
        if (completed) await run();
        else Assert.Equal("incomplete", (await Assert.ThrowsAsync<ChatGptException>(run)).Code);
        Assert.Equal("Bonjour", text.ToString());
        Assert.Equal("rotated-refresh", Load().RefreshToken);
        Assert.Equal("new-access", Load().AccessToken);
        Assert.True(ChatGptOAuth.HasPlan(Load().Scopes));
    }

    private static string Response(HttpRequestMessage request, bool completed)
    {
        Assert.Equal("new-access", request.Headers.Authorization?.Parameter);
        using var payload = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
        Assert.True(payload.RootElement.GetProperty("stream").GetBoolean());
        Assert.False(payload.RootElement.GetProperty("store").GetBoolean());
        Assert.Equal("test-model", payload.RootElement.GetProperty("model").GetString());
        return "data: {\"type\":\"response.output_text.delta\",\"delta\":\"Bonjour\"}\n\n"
            + (completed ? "data: {\"type\":\"response.completed\"}\n\n" : "");
    }
}
