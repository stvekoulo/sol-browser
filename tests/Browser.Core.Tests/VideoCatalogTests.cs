using Browser.Core;

namespace Browser.Core.Tests;

public class VideoCatalogTests
{
    [Fact]
    public void HlsKeepsTheMasterForSeparateAudioAndListsOnlyAdvertisedQualities()
    {
        const string url = "https://cdn.example/master.m3u8?key=example";
        var result = VideoVariants.Hls(url, "#EXTM3U\n#EXT-X-MEDIA:TYPE=AUDIO,URI=\"audio.m3u8\"\n#EXT-X-STREAM-INF:RESOLUTION=1280x720\n720.m3u8\n#EXT-X-STREAM-INF:RESOLUTION=1920x1080\n1080.m3u8");
        Assert.Equal([720, 1080], result.Select(v => v.Height));
        Assert.All(result, v => Assert.Equal(url, v.Url));
        Assert.Equal(1080, VideoQualitySelection.Pick(result.Select(v => v.Height), 1440));
    }

    [Fact]
    public void DashRejectsDrmAndExternalEntities()
    {
        Assert.Empty(VideoVariants.Dash("https://cdn.example/a.mpd", "<MPD><ContentProtection/><Representation height=\"1080\"/></MPD>"));
        Assert.Empty(VideoVariants.Dash("https://cdn.example/a.mpd", "<!DOCTYPE x [<!ENTITY x SYSTEM 'file:///secret'>]><MPD>&x;</MPD>"));
        Assert.Equal([720, 1080], VideoVariants.Dash("https://cdn.example/a.mpd", "<MPD><Representation height=\"720\"/><Representation height=\"1080\"/></MPD>").Select(v => v.Height));
    }

    [Fact]
    public void WholeFileRemovesOnlyUnsignedFragmentParametersOnMediaHost()
    {
        const string url = "https://r1.googlevideo.com/videoplayback?itag=137&range=0-99&rn=1&sparams=itag&sig=demo";
        var whole = VideoVariants.WholeFile(url);
        Assert.Null(VideoVariants.Query(whole, "range"));
        Assert.Equal("demo", VideoVariants.Query(whole, "sig"));
        Assert.Equal("0-99", VideoVariants.Query(VideoVariants.WholeFile(url.Replace("sparams=itag", "sparams=itag,range")), "range"));
        Assert.Equal(url.Replace("r1.googlevideo.com", "example.com"), VideoVariants.WholeFile(url.Replace("r1.googlevideo.com", "example.com")));
    }
}
