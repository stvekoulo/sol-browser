using System.Text.Json;

namespace Browser.Core;

public sealed class Settings
{
    public string SearchUrlTemplate { get; set; } = "https://www.google.com/search?q={0}";
    public bool BlurEnabled { get; set; } = true;
    public string HomeUrl { get; set; } = "about:blank";
}

public sealed class Session
{
    public List<string> Urls { get; set; } = new();
    public int ActiveIndex { get; set; }
    public int? SecondaryIndex { get; set; }
    public double Ratio { get; set; } = 0.5;
}

public static class JsonFileStore
{
    public static T Load<T>(string path) where T : new()
    {
        if (!File.Exists(path)) return new T();
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path)) ?? new T();
        }
        catch (JsonException)
        {
            File.Move(path, path + ".corrupt", overwrite: true);
            return new T();
        }
    }

    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value));
        File.Move(tmp, path, overwrite: true);
    }
}
