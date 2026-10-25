using System.Text.Json;

namespace Browser.Core.Tests;

public sealed class SolAiDiagnosticsTests
{
    private static object Request(int status, string id = "r1") => new { id, method = "GET", url = "https://example.org/api?token=fixture-private-value",
        type = "Fetch", status, durationMs = (double?)42, size = 100L, failed = (string?)null,
        requestHeaders = new { Authorization = "fixture-private-value" }, postData = "private body excluded" };

    [Fact]
    public void SummaryFiltersFailuresAndRedactsSecretsWithoutSendingBodies()
    {
        var export = JsonSerializer.Serialize(new { requests = new[] { Request(503), Request(200, "ok"), Request(0, "pending") },
            console = new[] { new { level = "error", text = "TypeError; token=fixture-private-value" }, new { level = "log", text = "normal" } } });
        var result = SolAiDiagnostics.Report(export, "", true);
        using var doc = JsonDocument.Parse(result);
        Assert.Single(doc.RootElement.GetProperty("requests").EnumerateArray());
        Assert.Single(doc.RootElement.GetProperty("console").EnumerateArray());
        Assert.DoesNotContain("fixture-private-value", result);
        Assert.DoesNotContain("private body excluded", result);
        Assert.DoesNotContain("requestHeaders", result);
        Assert.Equal("r1", doc.RootElement.GetProperty("requests")[0].GetProperty("id").GetString());
        Assert.Contains("pending status 0", result);
    }

    [Fact]
    public void BoundsAndFiltersLargeReportsAndKeepsValidJson()
    {
        var export = JsonSerializer.Serialize(new { requests = Enumerable.Range(0, 400).Select(i => Request(503, i.ToString())),
            console = Enumerable.Range(0, 200).Select(i => new { level = "error", text = "Failure " + new string('x', 4000) }) });
        var report = SolAiDiagnostics.Report(export, "", true);
        using var doc = JsonDocument.Parse(report);
        Assert.True(doc.RootElement.GetProperty("truncated").GetBoolean());
        Assert.Equal(25, doc.RootElement.GetProperty("requests").GetArrayLength());
        Assert.Equal(15, doc.RootElement.GetProperty("console").GetArrayLength());
        Assert.True(report.Length < 40000);
        using var filtered = JsonDocument.Parse(SolAiDiagnostics.Report(export, "no-match", true));
        Assert.Empty(filtered.RootElement.GetProperty("requests").EnumerateArray());
    }

    [Fact]
    public void MasksCompleteStructuredContentBeforeTruncating()
    {
        var text = JsonSerializer.Serialize(new { token = "fixture-private-value", description = new string('x', 10000) });
        var clipped = SolAiDiagnostics.Clip(text, 100);
        Assert.DoesNotContain("fixture-private-value", clipped);
        Assert.EndsWith("…", clipped);
    }
}
