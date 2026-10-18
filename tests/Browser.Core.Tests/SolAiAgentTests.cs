using System.Text.Json;
using Browser.Core;

namespace Browser.Core.Tests;

public sealed class SolAiAgentTests
{
    [Fact]
    public void ReadsFunctionCallsAndDropsIds()
    {
        var (call, item) = SolAiAgent.OutputItem("""{"type":"response.output_item.done","item":{"id":"fc_1","type":"function_call","call_id":"c1","name":"find_in_page","arguments":"{\"query\":\"prix\"}"}}""");
        Assert.Equal(new AgentCall("c1", "find_in_page", """{"query":"prix"}"""), call);
        Assert.False(item!.Value.TryGetProperty("id", out _));
        Assert.Equal("prix", SolAiAgent.Argument(call!.Arguments, "query"));
        var (none, reasoning) = SolAiAgent.OutputItem("""{"type":"response.output_item.done","item":{"id":"rs_1","type":"reasoning","encrypted_content":"x"}}""");
        Assert.Null(none);
        Assert.Equal("x", reasoning!.Value.GetProperty("encrypted_content").GetString());
        Assert.Equal((null, null), SolAiAgent.OutputItem("""{"type":"response.output_text.delta","delta":"a"}"""));
        Assert.Equal("", SolAiAgent.Argument("not json", "query"));
    }

    [Fact]
    public void OnlySiteChangesNeedApproval()
    {
        Assert.True(SolAiAgent.NeedsApproval("click"));
        Assert.True(SolAiAgent.NeedsApproval("navigate"));
        Assert.False(SolAiAgent.NeedsApproval("read_link"));
        Assert.All(SolAiAgent.PageTools.Concat(SolAiAgent.NoteTools), t => Assert.Equal("function", JsonSerializer.SerializeToElement(t).GetProperty("type").GetString()));
    }

    [Fact]
    public void SearchesNotesByTitleThenText()
    {
        var now = DateTimeOffset.UtcNow;
        QuickNote[] notes = [new("1", "Courses", "lait", "", now), new("2", "Réunion", "budget courses", "", now.AddDays(1)), new("3", "Autre", "rien", "", now) { Deleted = now }];
        Assert.Equal(["1", "2"], SolAiAgent.SearchNotes(notes, "courses").Select(n => n.Id));
        Assert.Equal(["2", "1"], SolAiAgent.SearchNotes(notes, "").Select(n => n.Id));
    }

    [Fact]
    public void ExtractsReadableTextFromHtml()
    {
        var text = SolAiAgent.HtmlText("<html><style>p{}</style><script>alert(1)</script><h1>Guide</h1><p>Installer &amp; lancer</p><nav>a</nav></html>");
        Assert.Equal("Guide\nInstaller & lancer\na", text);
    }
}
