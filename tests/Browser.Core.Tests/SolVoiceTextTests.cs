using Browser.Core;

namespace Browser.Core.Tests;

public sealed class SolVoiceTextTests
{
    [Fact]
    public void ReadsHumanTextWithoutMarkdownCodeOrRawUrls()
    {
        Assert.Equal("Bonjour. Ouvrir Sol. Guide", SolVoiceText.ForSpeech("# Bonjour.\n\nOuvrir **Sol**. [Guide](https://example.org)\n```js\nconst secret='not spoken';\n```\nhttps://example.org/long"));
        Assert.Equal(4000, SolVoiceText.ForSpeech(new string('a', 5000)).Length);
    }
    [Theory]
    [InlineData("Oui !", true)]
    [InlineData("je confirme", true)]
    [InlineData("non.", false)]
    [InlineData("Cancel", false)]
    [InlineData("oui mais attends", null)]
    [InlineData("ouvre youtube", null)]
    public void VoiceApprovalRequiresAnUnambiguousAnswer(string text, bool? expected) => Assert.Equal(expected, SolVoiceText.Approval(text));
    [Theory]
    [InlineData("voice.open")]
    [InlineData("voice.listen")]
    [InlineData("voice.stop")]
    [InlineData("voice.close")]
    [InlineData("voice.settings")]
    public void VoiceCommandsAreAcceptedByTheNativeBridge(string type) => Assert.NotNull(CommandParser.Parse("{\"type\":\"" + type + "\"}"));
}
