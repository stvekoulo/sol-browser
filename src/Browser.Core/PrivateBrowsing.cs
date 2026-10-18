namespace Browser.Core;

public static class PrivateBrowsing
{
    public const string ProfilePrefix = "sol-private-";
    public static bool IsProfile(string name) => name.StartsWith(ProfilePrefix, StringComparison.Ordinal)
        && TabSessions.ValidId(name[ProfilePrefix.Length..]);
}
