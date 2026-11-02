using System.Text.Json;

namespace Browser.Core;

public static partial class Backup
{
    private sealed record Replacement(string Name, bool Existed);

    /// <summary>False signifie aucune préparation. Un échec bloque le démarrage ; son journal reste récupérable.</summary>
    public static bool ApplyStaged(string dataDir)
    {
        CleanupCompleted(dataDir);
        var transaction = Path.Combine(dataDir, "restore-transaction");
        if (Directory.Exists(transaction))
        {
            if (File.Exists(Path.Combine(transaction, "committed")))
            {
                FinishRestore(dataDir, transaction);
                return true;
            }
            Rollback(dataDir, transaction);
        }
        if (!Directory.Exists(Staging(dataDir))) return false;
        var (staged, ready) = ReadPrepared(dataDir);
        PrepareTransaction(dataDir, staged, ready, transaction);
        try
        {
            foreach (var replacement in ReadReplacements(transaction))
            {
                var live = Path.Combine(dataDir, replacement.Name);
                var original = Path.Combine(transaction, "old", replacement.Name);
                var candidate = Path.Combine(transaction, "new", replacement.Name);
                if (replacement.Existed) MovePath(live, original);
                if (Exists(candidate)) MovePath(candidate, live);
            }
            // Publication du commit après tous les remplacements : avant lui, une reprise annule tout.
            DurableWrite(Path.Combine(transaction, "committed.tmp"), "complete"u8.ToArray());
            File.Move(Path.Combine(transaction, "committed.tmp"), Path.Combine(transaction, "committed"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            try { Rollback(dataDir, transaction); }
            catch (Exception rollback) when (rollback is IOException or UnauthorizedAccessException)
            { throw new IOException("Restauration interrompue. Fermez les programmes utilisant le profil, puis relancez Sol pour récupérer son état.", new AggregateException(e, rollback)); }
            throw new IOException("Restauration annulée. Le profil précédent est conservé ; fermez les programmes utilisant ses fichiers puis relancez Sol.", e);
        }
        FinishRestore(dataDir, transaction);
        return true;
    }

    private static void PrepareTransaction(string dataDir, string staged, Ready ready, string transaction)
    {
        var temporary = Path.Combine(dataDir, ".restore-transaction-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(temporary, "new"));
        Directory.CreateDirectory(Path.Combine(temporary, "old"));
        try
        {
            var replacements = ready.Targets.Distinct().Select(name => new Replacement(name, Exists(Path.Combine(dataDir, name)))).ToArray();
            foreach (var replacement in replacements)
            {
                var source = Path.Combine(staged, replacement.Name);
                var target = Path.Combine(temporary, "new", replacement.Name);
                if (File.Exists(source) && replacement.Name == PendingPasswords)
                    DurableWrite(target, PasswordBackup.MergePending(Path.Combine(dataDir, PendingPasswords), File.ReadAllBytes(source)));
                else if (File.Exists(source)) CopyDurable(source, target);
                else if (Directory.Exists(source)) CopyDirectory(source, target);
            }
            DurableWrite(Path.Combine(temporary, "plan.json"), JsonSerializer.SerializeToUtf8Bytes(replacements));
            Directory.Move(temporary, transaction);
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
    }

    private static Replacement[] ReadReplacements(string transaction)
    {
        Replacement[]? plan;
        try { plan = JsonSerializer.Deserialize<Replacement[]>(File.ReadAllBytes(Path.Combine(transaction, "plan.json"))); }
        catch (JsonException e) { throw new InvalidDataException("Journal de restauration invalide : profil non ouvert.", e); }
        if (plan is null || plan.Any(item => item is null || !RestoreTarget(item.Name)) || plan.Select(item => item.Name).Distinct().Count() != plan.Length)
            throw new InvalidDataException("Journal de restauration invalide : profil non ouvert.");
        return plan;
    }

    private static void Rollback(string dataDir, string transaction)
    {
        foreach (var replacement in ReadReplacements(transaction).Reverse())
        {
            var live = Path.Combine(dataDir, replacement.Name);
            var original = Path.Combine(transaction, "old", replacement.Name);
            if (Exists(original))
            {
                DeletePath(live);
                MovePath(original, live);
            }
            else if (!replacement.Existed) DeletePath(live);
        }
        Directory.Delete(transaction, true);
    }

    private static void FinishRestore(string dataDir, string transaction)
    {
        if (Directory.Exists(Staging(dataDir))) Directory.Delete(Staging(dataDir), true);
        // Ne jamais effacer partiellement le journal encore nommé « transaction » : perdre son marqueur
        // de commit pendant le nettoyage pourrait déclencher un rollback sur des originaux déjà effacés.
        var completed = Path.Combine(dataDir, ".restore-completed-" + Guid.NewGuid().ToString("N"));
        Directory.Move(transaction, completed);
        CleanupCompleted(dataDir);
    }

    private static void CleanupCompleted(string dataDir)
    {
        if (!Directory.Exists(dataDir)) return;
        foreach (var directory in Directory.EnumerateDirectories(dataDir, ".restore-completed-*"))
        {
            if (!Guid.TryParseExact(Path.GetFileName(directory)[19..], "N", out _)) continue;
            try { Directory.Delete(directory, true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* ancien profil, hors transaction */ }
        }
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
    private static void MovePath(string source, string destination)
    {
        if (Directory.Exists(source)) Directory.Move(source, destination);
        else File.Move(source, destination);
    }

    private static void DeletePath(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, true);
        else File.Delete(path);
    }

    private static void CopyDurable(string source, string destination)
    {
        using var input = File.OpenRead(source);
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        input.CopyTo(output);
        output.Flush(flushToDisk: true);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            CopyDurable(file, Path.Combine(destination, Path.GetRelativePath(source, file)));
    }
}
