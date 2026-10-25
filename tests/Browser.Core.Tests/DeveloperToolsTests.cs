using Browser.Core;
using System.Text.Json;
namespace Browser.Core.Tests;
public class DeveloperToolsTests
{
    [Theory]
    [InlineData("http://localhost:5173/a?q=1#example",true)]
    [InlineData("http://127.0.0.1:3000/",true)]
    [InlineData("http://[::1]:3000/",true)]
    [InlineData("https://localhost.example/",false)]
    [InlineData("http://user:secret@localhost/",false)]
    public void LanPreviewIsLimitedToLocalDevelopment(string url,bool valid)=>Assert.Equal(valid,LanPreview.IsLocal(url));
    [Fact] public void RewritingPreservesPortPathAndQuery()
    {
        Assert.Equal("http://192.168.1.5:5173/a?q=1#example",LanPreview.Rewrite("http://localhost:5173/a?q=1#example","192.168.1.5"));
        Assert.Null(LanPreview.Rewrite("https://example.com/","192.168.1.5"));
        Assert.Null(LanPreview.Rewrite("http://localhost/","0.0.0.0"));
    }
    [Fact] public void WorkspaceRetainsLayoutAndDevices()
    {
        var original=new WorkspaceSnapshot("a","Projet",DateTimeOffset.UtcNow,new Session{Urls=["http://localhost/a","http://localhost/b"],Devices=["compare","iPhone SE|L"],PrimaryIndex=0,SecondaryIndex=1,Ratio=.6},["iPhone SE","iPad mini"]);
        var copy=JsonSerializer.Deserialize<WorkspaceSnapshot>(JsonSerializer.Serialize(original))!;
        Assert.Equal(original.Session.Devices,copy.Session.Devices);Assert.Equal(1,copy.Session.SecondaryIndex);Assert.Equal(.6,copy.Session.Ratio);Assert.Equal(original.ComparisonDevices,copy.ComparisonDevices);
    }
}
