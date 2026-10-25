using Browser.Core;
namespace Browser.Core.Tests;
public class LinkPreviewTests
{
    [Theory]
    [InlineData("https://example.com/article?q=test#section",true)]
    [InlineData("http://localhost:3000/",true)]
    [InlineData("javascript:alert(1)",false)]
    [InlineData("file:///C:/secret",false)]
    [InlineData("https://user:password@example.com/",false)]
    [InlineData("data:text/html,hi",false)]
    public void LimitsPreviewToWebDocuments(string url,bool expected)=>Assert.Equal(expected,LinkPreview.Allowed(url));
}
