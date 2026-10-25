namespace Browser.Core;

public static class LinkPreview
{
    public static bool Allowed(string? url) => url is { Length: > 0 and <= 16000 }
        && Uri.TryCreate(url,UriKind.Absolute,out var uri) && uri.Scheme is "http" or "https" && uri.UserInfo.Length==0;
}
