using System.Text.Json;
using System.Text;
using System.IO.Compression;

namespace Browser.Core;

public static class NoteTransfer
{
    public const int MaxImport = 150 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, RespectNullableAnnotations = true, WriteIndented = true };
    public static string Export(IEnumerable<QuickNote> notes, string format)
    {
        var items = notes.Where(n => n.Deleted is null).ToList();
        if (format == "json") return JsonSerializer.Serialize(items, Json);
        if (format == "csv")
        {
            string Quote(string text) => "\"" + text.Replace("\"", "\"\"") + "\"";
            return "id,title,content,type,tags,url,createdAt,updatedAt,solEscaped\r\n" + string.Join("\r\n", items.Select(n => {
                var values=new[] {n.Id,n.Title,n.Text,n.Kind,string.Join(',',n.Tags),n.Url,(n.Created == default ? n.Updated : n.Created).ToString("O"),n.Updated.ToString("O")};
                var escaped=new List<int>();for(var i=0;i<values.Length;i++)if(values[i].TrimStart() is {Length:>0} value&&"=+@-".Contains(value[0])){values[i]="'"+values[i];escaped.Add(i);}
                return string.Join(',',values.Append(string.Join('|',escaped)).Select(Quote));
            }));
        }
        return string.Join("\n\n---\n\n", items.Select(n => $"# {n.Title}\n\n{n.Text}\n\n{string.Join(' ',n.Tags.Select(t=>"#"+t))}\n\n{n.Url}"));
    }
    public static IReadOnlyList<QuickNote>? Parse(string text, string name)
    {
        if (text.Length > MaxImport) return null;
        text=text.TrimStart('\uFEFF');
        try
        {
            if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                using var doc = JsonDocument.Parse(text);
                var list = doc.RootElement;
                if (list.ValueKind == JsonValueKind.Object && list.TryGetProperty("notes",out var nested)) list = nested;
                if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() > 1000) return null;
                var items = new List<QuickNote>();
                foreach (var n in list.EnumerateArray())
                {
                    if(n.ValueKind!=JsonValueKind.Object)return null;
                    if (n.TryGetProperty("content",out var content))
                    {
                        var body = content.GetString() ?? "";
                        var kind = n.TryGetProperty("type",out var type) && type.TryGetInt32(out var number) ? number switch {0=>"idea",2=>"task",_=>"note"} : "note";
                        var tags = n.TryGetProperty("tags",out var ts) && ts.ValueKind == JsonValueKind.Array ? ts.EnumerateArray().Select(t=>
                            t.ValueKind == JsonValueKind.String ? t.GetString()! : t.TryGetProperty("name",out var title) ? title.GetString()! : "").Where(t=>t.Length>0).ToArray() : System.Text.RegularExpressions.Regex.Matches(body,@"(?:^|\s)#([\p{L}\p{N}_/-]{1,64})").Select(m=>m.Groups[1].Value).Distinct().ToArray();
                        var created=n.TryGetProperty("createdAt",out var a)&&a.TryGetDateTimeOffset(out var born)?born:DateTimeOffset.UtcNow;
                        var updated = n.TryGetProperty("updatedAt",out var u) && u.TryGetDateTimeOffset(out var date) ? date : created;
                        var id=n.TryGetProperty("id",out var key)?key.ValueKind==JsonValueKind.String?key.GetString()!:key.GetRawText():"";
                        items.Add(new(id,body.Split('\n')[0].TrimStart('#',' ')[..Math.Min(120,body.Split('\n')[0].TrimStart('#',' ').Length)],body,"",updated) { Kind=kind,Tags=tags,Created=created,
                            Pinned=n.TryGetProperty("isTop",out var pin)&&pin.ValueKind==JsonValueKind.True,Archived=n.TryGetProperty("isArchived",out var archive)&&archive.ValueKind==JsonValueKind.True });
                    }
                    else if (JsonSerializer.Deserialize<QuickNote>(n.GetRawText(),Json) is { } note) items.Add(note);
                    else return null;
                }
                return items.Count > 0 && items.All(n=>QuickNotes.Valid(n,allowEmpty:n is {Id:{Length:>0}})) ? items : null;
            }
            if (name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                using var reader = new Microsoft.VisualBasic.FileIO.TextFieldParser(new StringReader(text)) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
                reader.SetDelimiters(","); var header = reader.ReadFields()?.Select(h=>h.Replace(" ","").ToLowerInvariant()).ToArray(); if (header is null) return null;
                var items = new List<QuickNote>();
                while (!reader.EndOfData && items.Count <= 1000)
                {
                    var row = reader.ReadFields(); if(row is null || row.Length != header.Length)return null;
                    var escapedColumn=Array.IndexOf(header,"solescaped");var escaped=escapedColumn<0?[]:row[escapedColumn].Split('|');
                    string Value(string key) {var index=Array.IndexOf(header,key.ToLowerInvariant());return index<0?"":escaped.Contains(index.ToString())&&row[index].StartsWith('\'')?row[index][1..]:row[index];}
                    var body=Value("content"); if(body.Length==0)body=Value("text");
                    items.Add(new(Value("id"),Value("title"),body,Value("url"),DateTimeOffset.TryParse(Value("updatedAt"),out var date)?date:DateTimeOffset.UtcNow)
                        {Kind=Value("type") is "idea" or "task"?Value("type"):"note",Tags=Value("tags").Split(',',StringSplitOptions.RemoveEmptyEntries),Created=DateTimeOffset.TryParse(Value("createdAt"),out var created)?created:default});
                }
                return items.Count is > 0 and <= 1000 && items.All(n=>QuickNotes.Valid(n,allowEmpty:n.Id.Length>0)) ? items : null;
            }
            var title = Path.GetFileNameWithoutExtension(name); if(title.Length>120)title=title[..120];
            var markdown = new QuickNote("",title,text,"",DateTimeOffset.UtcNow);
            return QuickNotes.Valid(markdown) ? [markdown] : null;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or Microsoft.VisualBasic.FileIO.MalformedLineException) { return null; }
    }
    public static byte[] Archive(IEnumerable<QuickNote> notes, NoteFiles files)
    {
        var items=notes.ToList();using var stream=new MemoryStream();
        using(var zip=new ZipArchive(stream,ZipArchiveMode.Create,true))
        {
            using(var writer=new StreamWriter(zip.CreateEntry("notes.json").Open(),new UTF8Encoding(false)))writer.Write(JsonSerializer.Serialize(items,Json));
            foreach(var id in items.SelectMany(n=>n.Files).Distinct())if(files.Find(id) is { } file)
            { using var target=zip.CreateEntry("attachments/"+id+"/"+file.Name).Open();target.Write(file.Data); }
        }
        return stream.ToArray();
    }
    public static (IReadOnlyList<QuickNote> Notes, Dictionary<string,(string Name,byte[] Data)> Files)? OpenArchive(byte[] data)
    {
        if(data.Length>MaxImport)return null;
        try
        {
            using var zip=new ZipArchive(new MemoryStream(data),ZipArchiveMode.Read);
            if(zip.Entries.Count>1001||zip.Entries.Sum(e=>e.Length)>MaxImport)return null;
            var entry=zip.GetEntry("notes.json");if(entry is null){var csv=zip.GetEntry("notes.csv");if(csv is null)return NoteMarkdownArchive.Read(zip);var parsed=Parse(Encoding.UTF8.GetString(ReadEntry(csv,MaxImport)),"notes.csv");return parsed is null?null:(parsed,new Dictionary<string,(string,byte[])>());}
            var json=ReadEntry(entry,MaxImport);var total=json.Length;
            var notes=Parse(Encoding.UTF8.GetString(json),"notes.json");
            if(notes is null)return null;
            var files=new Dictionary<string,(string,byte[])>();
            foreach(var id in notes.SelectMany(n=>n.Files).Distinct())
            {
                var asset=zip.Entries.FirstOrDefault(e=>e.FullName.StartsWith("attachments/"+id+"/",StringComparison.Ordinal));
                if(asset is null||asset.Length>NoteFiles.MaxFile)return null;
                var bytes=ReadEntry(asset,NoteFiles.MaxFile);total+=bytes.Length;if(total>MaxImport)return null;
                files[id]=(Path.GetFileName(asset.FullName),bytes);
            }
            return (notes,files);
        }
        catch(Exception e) when(e is IOException or InvalidDataException or ArgumentException){return null;}
    }
    internal static byte[] ReadEntry(ZipArchiveEntry entry,int limit)
    {
        using var source=entry.Open();using var output=new MemoryStream();var buffer=new byte[8192];int count;
        while((count=source.Read(buffer,0,buffer.Length))>0){if(output.Length+count>limit)throw new InvalidDataException("Archive entry too large");output.Write(buffer,0,count);}
        return output.ToArray();
    }
}
