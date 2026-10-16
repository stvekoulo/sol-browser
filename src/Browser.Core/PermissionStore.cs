namespace Browser.Core;

/// <summary>
/// Réponses déjà données aux demandes d'accès des sites (micro, caméra, position, notifications…), par site et
/// par type d'accès. Sans réponse enregistrée, le navigateur redemande.
/// </summary>
public sealed class PermissionStore(string path)
{
    private Dictionary<string, bool>? _answers;

    // Une ancienne copie ne doit jamais rétablir un accès révoqué : fichier abîmé = redemander.
    private Dictionary<string, bool> Answers => _answers ??= JsonFileStore.Load<Dictionary<string, bool>>(path, recoverBackup: false);

    /// <summary>true = autorisé, false = refusé, null = jamais demandé.</summary>
    public bool? Get(string origin, string kind) => Answers.TryGetValue(Key(origin, kind), out var allowed) ? allowed : null;

    public bool Set(string origin, string kind, bool allowed)
    {
        var next = new Dictionary<string, bool>(Answers) { [Key(origin, kind)] = allowed };
        return Save(next);
    }

    /// <summary>Oublie toutes les réponses : chaque site redemandera.</summary>
    public bool Clear()
    {
        return Save(new());
    }

    /// <summary>Oublie les réponses données à un site : il redemandera.</summary>
    public bool Forget(string origin)
    {
        return Save(Answers.Where(p => !p.Key.StartsWith(origin + " ", StringComparison.Ordinal)).ToDictionary());
    }

    private bool Save(Dictionary<string, bool> next)
    {
        if (!JsonFileStore.Save(path, next)) return false;
        _answers = next;
        return true;
    }

    public static string FileForProfile(string root, string profile) => profile is "" or "Default"
        ? Path.Combine(root, "permissions.json")
        : profile.StartsWith("sol-", StringComparison.Ordinal) && TabSessions.ValidId(profile[4..])
            ? Path.Combine(root, "session-permissions", profile[4..] + ".json")
            : throw new ArgumentException("Unknown permission profile", nameof(profile));

    public int Count => Answers.Count;

    /// <summary>Toutes les réponses retenues, site par site.</summary>
    public IReadOnlyList<(string Origin, string Kind, bool Allowed)> All =>
        Answers.Where(p => p.Key.Contains(' ')).Select(p => (p.Key[..p.Key.LastIndexOf(' ')], p.Key[(p.Key.LastIndexOf(' ') + 1)..], p.Value))
            .OrderBy(a => a.Item1, StringComparer.Ordinal).ToList();

    // L'origine (schéma + hôte + port) ne contient jamais d'espace : la clé est sans ambiguïté.
    private static string Key(string origin, string kind) => $"{origin} {kind}";

    /// <summary>Origine d'une adresse : « https://exemple.fr:8443 ». Null si l'adresse n'en a pas (about:, data:…).</summary>
    public static string? OriginOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" ? uri.GetLeftPart(UriPartial.Authority) : null;
}
