using Browser.Core;

namespace Browser.Core.Tests;

public class VideoPlaylistTests
{
    [Fact]
    public void WatchLinkTargetsItsPlaylistOnlyOnExplicitCollectionRequest()
    {
        Assert.Equal("https://www.youtube.com/playlist?list=PL_123", VideoPlaylist.CollectionUrl("https://www.youtube.com/watch?v=abc&list=PL_123&index=2"));
        Assert.Equal("https://example.com/watch?list=abc", VideoPlaylist.CollectionUrl("https://example.com/watch?list=abc"));
        Assert.Throws<ArgumentException>(() => VideoPlaylist.CollectionUrl("file:///secret"));
    }

    [Theory]
    [InlineData("{\"has_drm\":true,\"url\":\"https://example.com/video\"}")]
    [InlineData("{\"is_live\":true,\"url\":\"https://example.com/video\"}")]
    [InlineData("{\"live_status\":\"is_upcoming\",\"url\":\"https://example.com/video\"}")]
    [InlineData("{\"title\":\"[Private video]\",\"url\":\"https://example.com/video\"}")]
    [InlineData("{\"url\":\"file:///private\"}")]
    [InlineData("{\"url\":\"https://user:password@example.com/private\"}")]
    [InlineData("{\"_type\":\"playlist\",\"url\":\"https://example.com/other-list\"}")]
    public void UnavailableOrUnsafeEntriesAreNotSelectable(string json) => Assert.Null(VideoPlaylist.ParseEntry(json, 1));

    [Fact]
    public void FlatYoutubeEntriesUseIndividualVideoUrlsAndStableKeys()
    {
        var item = VideoPlaylist.ParseEntry("""{"id":"abcdefghijk","ie_key":"Youtube","title":"Episode 2","playlist_index":2}""", 1)!;
        Assert.Equal("https://www.youtube.com/watch?v=abcdefghijk", item.Url);
        Assert.Equal(2, item.Position);
        Assert.Equal(VideoPlaylist.Key(item.Url), item.Key);
        Assert.Equal("Episode 2", item.Title);
        var malformedIndex = VideoPlaylist.ParseEntry("""{"url":"https://example.com/video.mp4","playlist_index":"2"}""", 3)!;
        Assert.Equal(3, malformedIndex.Position);
        var generic = VideoPlaylist.ParseEntry("""{"url":"https://example.com/first.mp4","webpage_url":"https://example.com/collection"}""", 1)!;
        Assert.Equal("https://example.com/first.mp4", generic.Url);
    }

    [Theory]
    [InlineData("{\"type\":\"video.collection\",\"id\":\"a\",\"query\":\"playlist\",\"index\":1}")]
    [InlineData("{\"type\":\"video.batch\",\"id\":\"a\",\"query\":\"token\",\"input\":\"[]\",\"index\":720}")]
    public void CollectionCommandsReachTheController(string json) => Assert.NotNull(CommandParser.Parse(json));
}
