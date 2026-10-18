using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
namespace Browser.Core;
public static class NoteMarkdownArchive
{
    public static (IReadOnlyList<QuickNote> Notes,Dictionary<string,(string Name,byte[] Data)> Files)? Read(ZipArchive zip)
    {
        var entries=zip.Entries.Where(e=>e.FullName.EndsWith(".md",StringComparison.OrdinalIgnoreCase)||e.FullName.EndsWith(".txt",StringComparison.OrdinalIgnoreCase)).ToList();
        if(entries.Count is 0 or >1000)return null;
        var notes=new List<QuickNote>();var files=new Dictionary<string,(string,byte[])>();var paths=new Dictionary<string,string>();var total=0;
        foreach(var entry in entries)
        {
            var bytes=NoteTransfer.ReadEntry(entry,50000*4);total+=bytes.Length;if(total>NoteTransfer.MaxImport)return null;
            var parsed=NoteTransfer.Parse(Encoding.UTF8.GetString(bytes),Path.GetFileName(entry.FullName));if(parsed is null)return null;
            var note=parsed[0];var attached=new List<string>();
            foreach(Match match in Regex.Matches(note.Text,@"!?\[[^\]]*\]\(([^\s)]+)\)"))
            {
                if(!Uri.TryCreate(new Uri("https://archive.local/"+entry.FullName),match.Groups[1].Value,out var uri)||uri.Host!="archive.local")continue;
                var path=Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');var asset=zip.GetEntry(path);if(asset is null||asset==entry)continue;
                if(!paths.TryGetValue(path,out var id)){var data=NoteTransfer.ReadEntry(asset,NoteFiles.MaxFile);total+=data.Length;if(total>NoteTransfer.MaxImport)return null;id=Guid.NewGuid().ToString("N");paths[path]=id;files[id]=(Path.GetFileName(path),data);}
                if(!attached.Contains(id))attached.Add(id);
            }
            note=note with{Files=attached.ToArray(),Tags=Regex.Matches(note.Text,@"(?:^|\s)#([\p{L}\p{N}_/-]{1,64})").Select(m=>m.Groups[1].Value).Distinct().ToArray()};
            if(!QuickNotes.Valid(note))return null;notes.Add(note);
        }
        return (notes,files);
    }
}
