using System.Runtime.InteropServices;
using System.Text.Json;

namespace Browser.App;

internal static class StartupPreparation
{
    // Never use this for website permissions or view hardening: those must succeed before navigation.
    public static async Task OptionalAsync(string stage, Func<Task> prepare, Action<string, Exception> report)
    {
        try { await prepare(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or InvalidOperationException or COMException or ArgumentException or JsonException)
        { report(stage, error); }
    }
}
