using Browser.Core;

namespace Browser.Core.Tests;

public class VideoSourceTests
{
    [Theory]
    [InlineData("https://cdn.example/video.mp4?token=hidden", "", "file")]
    [InlineData("https://cdn.example/stream", "application/vnd.apple.mpegurl; charset=utf-8", "hls")]
    [InlineData("https://cdn.example/stream.mpd", "", "dash")]
    [InlineData("https://cdn.example/fragment.m4s", "video/mp4", null)]
    [InlineData("https://cdn.example/fragment.ts", "video/mp2t", null)]
    [InlineData("blob:https://example.com/123", "video/mp4", null)]
    [InlineData("file:///c:/video.mp4", "video/mp4", null)]
    [InlineData("https://user:password@example.com/video.mp4", "video/mp4", null)]
    [InlineData("https://example.com/page", "text/html", null)]
    public void ClassifiesWholeSourcesOnly(string url, string mime, string? kind) => Assert.Equal(kind, VideoSources.Kind(url, mime));

    [Theory]
    [InlineData(0, 2160)]
    [InlineData(1080, 1080)]
    [InlineData(1440, 1080)]
    [InlineData(720, 720)]
    [InlineData(480, 720)]
    [InlineData(4320, 2160)]
    public void DefaultQualityHasPredictableFallback(int preference, int expected) =>
        Assert.Equal(expected, VideoQualitySelection.Pick([2160, 720, 1080, 1080], preference));

    [Fact]
    public void ListsVideoFormatsAndExcludesDrmAndAudio()
    {
        var json = """{"formats":[{"height":1080,"vcodec":"avc1"},{"height":720,"vcodec":"vp9"},{"height":2160,"vcodec":"avc1","has_drm":true},{"height":480,"vcodec":"none"},{"height":360}]}""";
        Assert.Equal([1080, 720, 360], VideoQualitySelection.Parse(json));
        Assert.Throws<InvalidDataException>(() => VideoQualitySelection.Parse("""{"is_live":true}"""));
        Assert.Throws<InvalidDataException>(() => VideoQualitySelection.Parse("""{"has_drm":true}"""));
    }

    [Fact]
    public void RejectsCookieJarInjectionAndUnsafeFilenameCharacters()
    {
        var jar = VideoCookie.Jar([new(".example.com", "/", true, 0, "sid", "hello"), new(".example.com", "/", false, 0, "x", "bad\nother")]);
        Assert.Contains(".example.com\tTRUE\t/\tTRUE\t0\tsid\thello", jar);
        Assert.DoesNotContain("other", jar);
        Assert.DoesNotContain('%', VideoSources.FileStem("../%(filepath)s:video"));
        Assert.Equal(0, new Settings { VideoQuality = 1234 }.VideoQuality);
        Assert.Equal(2160, new Settings { VideoQuality = 2160 }.VideoQuality);
        Assert.Equal("bv*[height=720]+ba/b[height=720]", VideoQualitySelection.Selector(720, true));
    }
}
