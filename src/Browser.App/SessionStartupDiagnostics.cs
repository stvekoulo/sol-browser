using Browser.Core;

namespace Browser.App;

internal static class SessionStartupDiagnostics
{
    public static void Record(string stage, Exception error)
    {
        if (AppPaths.Private) return;
        try
        {
            // Persist only technical failures: never URLs, cookies, tokens or exception messages.
            var detail = $"{DateTimeOffset.UtcNow:o} stage={stage} type={error.GetType().FullName} hresult=0x{error.HResult:X8}\n{error.StackTrace}\n";
            System.IO.File.AppendAllText(Path.Combine(AppPaths.DataDir, "session-start-errors.log"), detail);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) { }
    }
}
