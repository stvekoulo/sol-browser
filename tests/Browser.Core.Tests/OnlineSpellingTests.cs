using System.Net;
using Browser.Core;
using Xunit;

public class OnlineSpellingTests
{
    private sealed class Stub(HttpStatusCode code, string body) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Calls++; return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body) }); }
    }

    private const string Sample = """
        {"matches":[
          {"offset":0,"length":3,"replacements":[{"value":"Les"}],"rule":{"issueType":"misspelling"}},
          {"offset":4,"length":6,"replacements":[{"value":"enfants"},{"value":"enfant"}],"rule":{"issueType":"grammar"}},
          {"offset":5,"length":2,"replacements":[],"rule":{"issueType":"style"}},
          {"offset":90,"length":5,"replacements":[],"rule":{"issueType":"grammar"}}]}
        """;

    [Fact]
    public void Parse_keeps_real_faults_in_bounds_and_classifies_them()
    {
        var issues = OnlineSpelling.Parse(Sample, 20);
        Assert.Equal(2, issues.Length);
        Assert.Equal(0, issues[0].Kind);
        Assert.Equal(1, issues[1].Kind);
        Assert.Equal(["enfants", "enfant"], issues[1].Suggestions);
    }

    [Fact]
    public async Task Quota_response_backs_off_for_a_minute()
    {
        var now = new DateTime(2026, 1, 1);
        var stub = new Stub(HttpStatusCode.TooManyRequests, "");
        var online = new OnlineSpelling(new HttpClient(stub), () => now);
        Assert.Null(await online.CheckAsync("un texte assez long", 3));
        now = now.AddSeconds(30);
        Assert.False(online.Worth("un texte assez long"));
        now = now.AddSeconds(31);
        Assert.True(online.Worth("un texte assez long"));
    }

    [Fact]
    public async Task Short_text_is_never_sent_and_requests_are_spaced()
    {
        var now = new DateTime(2026, 1, 1);
        var stub = new Stub(HttpStatusCode.OK, """{"matches":[]}""");
        var online = new OnlineSpelling(new HttpClient(stub), () => now);
        Assert.Null(await online.CheckAsync("bonjour", 3));
        Assert.Equal(0, stub.Calls);
        Assert.NotNull(await online.CheckAsync("un texte assez long", 3));
        Assert.Null(await online.CheckAsync("un texte assez long", 3));
        Assert.Equal(1, stub.Calls);
    }

    [Theory]
    [InlineData(1, "fr")]
    [InlineData(2, "en-US")]
    [InlineData(3, "auto")]
    public void Language_follows_setting(int mode, string language) => Assert.Equal(language, OnlineSpelling.Language(mode));
}
