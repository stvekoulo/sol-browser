namespace Browser.Core;

public sealed record NoteFile(string Id, string Name, string Mime, byte[] Data);

/// <summary>Pièces jointes dans un seul fichier de profil, inclus dans les sauvegardes ; mémoire seule en privé.</summary>
public sealed class NoteFiles(string? path)
{
    public const int MaxFile = 20 * 1024 * 1024, MaxTotal = 100 * 1024 * 1024;
    private List<NoteFile> _files = path is null ? [] : JsonFileStore.Load<List<NoteFile>>(path);
    public IReadOnlyList<NoteFile> All => _files;
    public NoteFile? Find(string id) => _files.Find(f => f.Id == id);
    public NoteFile? Add(string name, byte[] data)
    {
        if (data.Length is 0 or > MaxFile || _files.Sum(f => (long)f.Data.Length) + data.Length > MaxTotal) return null;
        var file=Create(name,data);
        return file is not null && Commit(_files.Append(file).ToList()) ? file : null;
    }
    public Dictionary<string,string>? Import(Dictionary<string,(string Name,byte[] Data)> files)
    {
        if(files.Count>1000||files.Values.Any(f=>f.Data.Length is 0 or >MaxFile)||_files.Sum(f=>(long)f.Data.Length)+files.Values.Sum(f=>(long)f.Data.Length)>MaxTotal)return null;
        var next=new List<NoteFile>();var ids=new Dictionary<string,string>();
        foreach(var (id,file) in files){var item=Create(file.Name,file.Data);if(item is null)return null;next.Add(item);ids[id]=item.Id;}
        return Commit(_files.Concat(next).ToList())?ids:null;
    }
    private static NoteFile? Create(string name,byte[] data)
    {
        name = Path.GetFileName(name);
        name = new string(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());
        if (name.Length is 0 or > 180) return null;
        var mime = data.AsSpan().StartsWith(new byte[] {137,80,78,71,13,10,26,10}) ? "image/png"
            : data.AsSpan().StartsWith(new byte[] {255,216,255}) ? "image/jpeg"
            : data.AsSpan().StartsWith("GIF8"u8) ? "image/gif"
            : data.Length >= 12 && data.AsSpan(0,4).SequenceEqual("RIFF"u8) && data.AsSpan(8,4).SequenceEqual("WEBP"u8) ? "image/webp"
            : data.AsSpan().StartsWith("%PDF-"u8) ? "application/pdf"
            : data.AsSpan().StartsWith("ID3"u8) || name.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ? "audio/mpeg"
            : name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ? "audio/wav"
            : name.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) ? "audio/webm" : "application/octet-stream";
        return new NoteFile(Guid.NewGuid().ToString("N"), name, mime, data);
    }
    public bool Keep(IReadOnlySet<string> ids) => _files.All(f=>ids.Contains(f.Id))||Commit(_files.Where(f => ids.Contains(f.Id)).ToList());
    private bool Commit(List<NoteFile> next)
    {
        if (path is not null && !JsonFileStore.Save(path, next)) return false;
        _files = next; return true;
    }
}
