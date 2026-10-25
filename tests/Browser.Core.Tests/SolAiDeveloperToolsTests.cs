using Browser.App;
using System.Text.Json;

namespace Browser.Core.Tests;

public sealed class SolAiDeveloperToolsTests
{
    private static string Mock(string mode = "response", string url = "https://example.org/api", int status = 503, int delay = 20) =>
        JsonSerializer.Serialize(new { id = "", url, method = "GET", mode, status, delay_ms = delay, body = "{\"error\":\"unavailable\"}" });

    [Fact]
    public async Task OpensLocalDiagnosticsAndControlsCaptureWithoutReturningLogs()
    {
        var browser = new BrowserController();browser.DeveloperTab();
        var result = await browser.DeveloperTool("start_diagnostics");
        Assert.True(browser.DeveloperPanelOpen);
        Assert.True(JsonDocument.Parse(result).RootElement.GetProperty("ok").GetBoolean());
        Assert.DoesNotContain("example.org", result);
        await browser.DeveloperTool("configure_diagnostics", """{"action":"pause"}""");Assert.True(browser.CapturePaused);
        await browser.DeveloperTool("configure_diagnostics", """{"action":"resume"}""");Assert.False(browser.CapturePaused);
    }

    [Fact]
    public async Task MissingCaptureIsReportedWithoutInventingErrors()
    {
        var browser = new BrowserController();browser.DeveloperTab();
        using var report = JsonDocument.Parse(await browser.DeveloperTool("read_diagnostics", """{"id":"","query":"","errors_only":true}"""));
        Assert.False(report.RootElement.GetProperty("capture").GetProperty("listening").GetBoolean());
        Assert.Empty(report.RootElement.GetProperty("diagnostics").GetProperty("requests").EnumerateArray());
        using var missing = JsonDocument.Parse(await browser.DeveloperTool("read_network_request", """{"id":"","request_id":"unknown"}"""));
        Assert.False(missing.RootElement.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task DeveloperToolsAndResponsiveSimulationUseValidatedDevices()
    {
        var browser = new BrowserController();var tab = browser.DeveloperTab();
        await browser.DeveloperTool("open_devtools");Assert.True(browser.DevToolsOpened(tab.Id));
        var device = JsonDocument.Parse(await browser.DeveloperTool("list_devices")).RootElement.GetProperty("devices")[0].GetProperty("Name").GetString();
        await browser.DeveloperTool("emulate_device", JsonSerializer.Serialize(new { device }));Assert.Equal(device, browser.EmulatedDevice);
        var failed = JsonDocument.Parse(await browser.DeveloperTool("emulate_device", """{"device":"invented"}"""));
        Assert.False(failed.RootElement.GetProperty("ok").GetBoolean());Assert.Equal(device, browser.EmulatedDevice);
        await browser.DeveloperTool("emulate_device", """{"device":""}""");Assert.Null(browser.EmulatedDevice);
    }

    [Fact]
    public async Task ActivatesAndRemovesExplicitMockWithoutReturningCapturedContents()
    {
        var browser = new BrowserController();browser.DeveloperTab();
        var result = await browser.DeveloperTool("mock_network", Mock());
        Assert.True(browser.MockActive);Assert.DoesNotContain("example.org", result);
        await browser.DeveloperTool("mock_network", Mock("disable"));Assert.False(browser.MockActive);
    }

    [Fact]
    public async Task RejectsPrivateInternalUnavailableAndCancelledContexts()
    {
        var browser = new BrowserController();var tab = browser.DeveloperTab(isPrivate: true);
        Assert.False(JsonDocument.Parse(await browser.DeveloperTool("start_diagnostics", JsonSerializer.Serialize(new { id = tab.Id }))).RootElement.GetProperty("ok").GetBoolean());
        browser.DeveloperTab("https://start.sol/index.html");
        Assert.False(JsonDocument.Parse(await browser.DeveloperTool("start_diagnostics")).RootElement.GetProperty("ok").GetBoolean());
        browser.DeveloperTab();browser.Ready = false;
        Assert.False(JsonDocument.Parse(await browser.DeveloperTool("open_devtools")).RootElement.GetProperty("ok").GetBoolean());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => browser.DeveloperTool("start_diagnostics", ct: new(true)));
    }

    [Theory]
    [InlineData("file:///C:/private", 503, 20)]
    [InlineData("https://user:password@example.org", 503, 20)]
    [InlineData("https://example.org/api", 99, 20)]
    [InlineData("https://example.org/api", 503, 10001)]
    public void InvalidMockCannotReachEngine(string url, int status, int delay) =>
        Assert.Throws<ArgumentException>(() => SolAiDeveloperTools.MockDraft(Mock(url: url, status: status, delay: delay)));

    [Fact]
    public void ExposesStrictSchemasAndPreservesApprovalPolicy()
    {
        foreach (var tool in SolAiDeveloperTools.Tools)
        {
            var schema = JsonSerializer.SerializeToElement(tool);
            Assert.True(schema.GetProperty("strict").GetBoolean());
            var parameters = schema.GetProperty("parameters");
            Assert.Equal(parameters.GetProperty("properties").EnumerateObject().Select(property => property.Name).Order(),
                parameters.GetProperty("required").EnumerateArray().Select(value => value.GetString()).Order());
        }
        foreach (var tool in new[] { "configure_diagnostics", "mock_network", "emulate_device", "read_diagnostics", "read_network_request" })
        {
            Assert.True(SolAiPermissions.RequiresApproval(tool, new()));
            Assert.False(SolAiPermissions.RequiresApproval(tool, new() { SolAiAutoApproveActions = true }));
        }
    }
}
