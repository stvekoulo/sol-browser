using System.Text.Json;

namespace Browser.Core;

// Lecture seule des anciennes installations WebView2, y compris celles des sessions non ouvertes.
public static class ExtensionMigration
{
    public static IReadOnlyDictionary<string, bool> Read(IEnumerable<string> profileFolders)
    {
        var result = new Dictionary<string, bool>();
        foreach (var folder in profileFolders)
            foreach (var name in new[] { "Secure Preferences", "Preferences" })
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, name)));
                    if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("extensions", out var extensions)
                        || extensions.ValueKind != JsonValueKind.Object || !extensions.TryGetProperty("settings", out var entries)
                        || entries.ValueKind != JsonValueKind.Object) continue;
                    foreach (var entry in entries.EnumerateObject())
                    {
                        if (!ExtensionCatalog.IsId(entry.Name) || entry.Value.ValueKind != JsonValueKind.Object) continue;
                        var value = entry.Value;
                        bool? enabled = null;
                        if (value.TryGetProperty("state", out var state))
                            enabled = state.ValueKind == JsonValueKind.Number && state.TryGetInt32(out var number) && number == 1;
                        if (value.TryGetProperty("disable_reasons", out var reasons))
                            enabled = reasons.ValueKind switch
                            {
                                JsonValueKind.Array => reasons.GetArrayLength() == 0 && enabled != false,
                                JsonValueKind.Number => reasons.TryGetInt32(out var flags) && flags == 0 && enabled != false,
                                _ => false,
                            };
                        if (enabled is { } on) result[entry.Name] = on && result.GetValueOrDefault(entry.Name, true);
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
            }
        return result;
    }
}
