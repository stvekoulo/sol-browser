using System.Security.Cryptography;
using System.Text.Json;
using Browser.Core;

namespace Browser.App;

public sealed partial class ChatGptAccount
{
    // Identifiant d'hôte opaque et stable, choisi avant la première connexion.
    private string HostId() => Locked(() =>
    {
        string? saved = null;
        try { saved = File.ReadAllText(_hostFile).Trim(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        var id = ChatGptOAuth.HostId(saved);
        if (id != saved) File.WriteAllText(_hostFile, id);
        return id;
    });

    private string RegistrationFile => _file + ".registration";
    private ChatGptRegistration? Registration()
    {
        if (Tokens is { } tokens) return new(tokens.ClientId, tokens.Subject, tokens.Email);
        try { return JsonSerializer.Deserialize<ChatGptRegistration>(ProtectedData.Unprotect(File.ReadAllBytes(RegistrationFile), Entropy, DataProtectionScope.CurrentUser)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException or JsonException) { return null; }
    }
    private void SaveRegistration(ChatGptRegistration registration)
    {
        File.WriteAllBytes(RegistrationFile + ".tmp", ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(registration), Entropy, DataProtectionScope.CurrentUser));
        File.Move(RegistrationFile + ".tmp", RegistrationFile, true);
    }

    private ChatGptTokens? Load()
    {
        try { return File.Exists(_file) ? JsonSerializer.Deserialize<ChatGptTokens>(ProtectedData.Unprotect(File.ReadAllBytes(_file), Entropy, DataProtectionScope.CurrentUser)) : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException or JsonException) { return null; }
    }
    // Écriture atomique, chiffrée pour l'utilisateur Windows ; jamais dans les journaux.
    private void Save(ChatGptTokens tokens)
    {
        var temp = _file + ".tmp";
        File.WriteAllBytes(temp, ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(tokens), Entropy, DataProtectionScope.CurrentUser));
        File.Move(temp, _file, true);
    }
    private T Locked<T>(Func<T> run)
    {
        using var mutex = new Mutex(false, _lock);
        try { mutex.WaitOne(); } catch (AbandonedMutexException) { }
        try { return run(); }
        finally { mutex.ReleaseMutex(); }
    }
    private void Locked(Action run) => Locked(() => { run(); return 0; });
}
