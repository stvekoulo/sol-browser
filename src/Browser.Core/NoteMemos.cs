using Microsoft.Data.Sqlite;
using System.Text.RegularExpressions;
namespace Browser.Core;

public static class NoteMemos
{
    public static IReadOnlyList<QuickNote>? Read(string path)
    {
        try
        {
            if(new FileInfo(path).Length>NoteTransfer.MaxImport)return null;
            using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());db.Open();
            using var trusted=db.CreateCommand();trusted.CommandText="PRAGMA trusted_schema=OFF";trusted.ExecuteNonQuery();
            var columns=new HashSet<string>();using(var schema=db.CreateCommand()){schema.CommandText="PRAGMA table_info(memo)";using var rows=schema.ExecuteReader();while(rows.Read())columns.Add(rows.GetString(1));}
            if(!new[]{"id","content","created_ts","updated_ts"}.All(columns.Contains))return null;
            using var command=db.CreateCommand();command.CommandText="SELECT id,content,created_ts,updated_ts,"+(columns.Contains("row_status")?"row_status":"''")+","+(columns.Contains("pinned")?"pinned":"0")+" FROM memo LIMIT 1001";
            var notes=new List<QuickNote>();using(var rows=command.ExecuteReader())while(rows.Read())
            {
                var text=rows.GetString(1);if(text.Length>50000)return null;
                var title=text.Split('\n')[0].TrimStart('#',' ');if(title.Length>120)title=title[..120];
                notes.Add(new("memo-"+rows.GetInt64(0),title,text,"",DateTimeOffset.FromUnixTimeSeconds(rows.GetInt64(3))){Kind="idea",Created=DateTimeOffset.FromUnixTimeSeconds(rows.GetInt64(2)),
                    Archived=rows.GetString(4)=="ARCHIVED",Pinned=rows.GetInt64(5)!=0,Tags=Regex.Matches(text,@"(?:^|\s)#([\p{L}\p{N}_/-]{1,64})").Select(m=>m.Groups[1].Value).Distinct().ToArray()});
            }
            if(notes.Count is 0 or >1000||notes.Any(n=>!QuickNotes.Valid(n)))return null;
            using var hasRelations=db.CreateCommand();hasRelations.CommandText="SELECT count(*) FROM sqlite_schema WHERE type='table' AND name='memo_relation'";
            if(Convert.ToInt32(hasRelations.ExecuteScalar())>0)
            {
                using var relations=db.CreateCommand();relations.CommandText="SELECT memo_id,related_memo_id,type FROM memo_relation LIMIT 1001";using var rows=relations.ExecuteReader();var comments=new HashSet<string>();
                while(rows.Read())
                {
                    var child=notes.FirstOrDefault(n=>n.Id=="memo-"+rows.GetInt64(0));var parentIndex=notes.FindIndex(n=>n.Id=="memo-"+rows.GetInt64(1));if(child is null||parentIndex<0)continue;
                    if(rows.GetString(2)=="COMMENT"){var parent=notes[parentIndex];if(child.Text.Length>5000)return null;notes[parentIndex]=parent with{Comments=parent.Comments.Append(new(child.Id,child.Text,child.Created)).ToArray()};comments.Add(child.Id);}
                    else if(rows.GetString(2)=="REFERENCE"){var index=notes.IndexOf(child);notes[index]=child with{Text=child.Text+"\n[["+notes[parentIndex].Id+"]]"};}
                }
                notes=notes.Where(n=>!comments.Contains(n.Id)).ToList();
            }
            return notes.Count>0&&notes.All(n=>QuickNotes.Valid(n))?notes:null;
        }
        catch(Exception e) when(e is SqliteException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or OverflowException){return null;}
    }
}
