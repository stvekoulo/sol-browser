using Browser.Core;

public class BackupRestoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("nav-restore").FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void A_locked_file_rolls_back_the_whole_restore_and_allows_retry()
    {
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "old settings");
        File.WriteAllText(Path.Combine(_dir, "session.json"), "old session");
        Backup.Stage(_dir, new Dictionary<string, byte[]>
        {
            ["settings.json"] = "new settings"u8.ToArray(),
            ["session.json"] = "new session"u8.ToArray(),
        });
        using (File.Open(Path.Combine(_dir, "session.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.ThrowsAny<IOException>(() => Backup.ApplyStaged(_dir));
            Assert.Equal("old settings", File.ReadAllText(Path.Combine(_dir, "settings.json")));
        }
        Assert.True(Backup.ApplyStaged(_dir));
        Assert.Equal("new settings", File.ReadAllText(Path.Combine(_dir, "settings.json")));
        Assert.Equal("new session", File.ReadAllText(Path.Combine(_dir, "session.json")));
    }

    [Fact]
    public void Empty_folders_and_absent_optional_files_replace_target_state()
    {
        var source = Directory.CreateDirectory(Path.Combine(_dir, "source")).FullName;
        Directory.CreateDirectory(Path.Combine(source, "extensions"));
        var target = Directory.CreateDirectory(Path.Combine(_dir, "target")).FullName;
        Directory.CreateDirectory(Path.Combine(target, "extensions", "old"));
        File.WriteAllText(Path.Combine(target, "extensions", "old", "main.js"), "old");
        File.WriteAllText(Path.Combine(target, "wallpaper.jpg"), "old");
        Backup.Stage(target, Backup.Collect(source));
        Assert.True(Backup.ApplyStaged(target));
        Assert.Empty(Directory.GetFiles(Path.Combine(target, "extensions"), "*", SearchOption.AllDirectories));
        Assert.False(File.Exists(Path.Combine(target, "wallpaper.jpg")));
    }

    [Fact]
    public void Legacy_backups_preserve_unrepresented_data_and_sqlite_journals()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "extensions", "old"));
        File.WriteAllText(Path.Combine(_dir, "browser.db-wal"), "journal");
        Backup.Stage(_dir, new Dictionary<string, byte[]> { ["settings.json"] = "new"u8.ToArray() });
        Assert.True(Backup.ApplyStaged(_dir));
        Assert.True(Directory.Exists(Path.Combine(_dir, "extensions", "old")));
        Assert.Equal("journal", File.ReadAllText(Path.Combine(_dir, "browser.db-wal")));
    }

    [Fact]
    public void An_unmarked_interrupted_preparation_is_not_applied()
    {
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "old");
        var stage = Directory.CreateDirectory(Path.Combine(_dir, "restore")).FullName;
        File.WriteAllText(Path.Combine(stage, "settings.json"), "partial");
        Assert.Throws<InvalidDataException>(() => Backup.ApplyStaged(_dir));
        Assert.Equal("old", File.ReadAllText(Path.Combine(_dir, "settings.json")));
    }

    [Fact]
    public void Unknown_zip_paths_are_filtered_and_ambiguous_output_paths_are_rejected()
    {
        var archive = Backup.Seal(new Dictionary<string, byte[]>
        {
            ["settings.json"] = [1], ["../evil"] = [2], ["extensions/a.js"] = [3],
        }, "test key");
        Assert.Equal(["extensions/a.js", "settings.json"], Backup.Open(archive, "test key")!.Keys.Order());
        Assert.Throws<InvalidDataException>(() => Backup.Seal(new Dictionary<string, byte[]> { ["extensions/a.js"] = [1], ["extensions/A.js"] = [2] }, "test key"));
    }

    [Fact]
    public void Sealing_refuses_an_archive_that_would_exceed_the_entry_limit()
    {
        var files = Enumerable.Range(0, 10001).ToDictionary(n => $"extensions/{n}.js", _ => Array.Empty<byte>());
        Assert.Throws<InvalidDataException>(() => Backup.Seal(files, "test key"));
    }

    [Fact]
    public void A_failed_stage_keeps_the_previous_complete_generation()
    {
        Backup.Stage(_dir, new Dictionary<string, byte[]> { ["settings.json"] = "complete"u8.ToArray() });
        Assert.ThrowsAny<Exception>(() => Backup.Stage(_dir, new Dictionary<string, byte[]> { ["profile-manifest.json"] = "broken"u8.ToArray() }));
        Assert.True(Backup.ApplyStaged(_dir));
        Assert.Equal("complete", File.ReadAllText(Path.Combine(_dir, "settings.json")));
    }

    [Fact]
    public void An_io_failure_halfway_through_staging_does_not_publish_partial_files()
    {
        Backup.Stage(_dir, new Dictionary<string, byte[]> { ["settings.json"] = "complete"u8.ToArray() });
        var conflictingPaths = new Dictionary<string, byte[]>
        {
            ["settings.json"] = "partial"u8.ToArray(),
            ["extensions/file"] = [1],
            ["extensions/file/child.js"] = [2],
        };
        Assert.ThrowsAny<IOException>(() => Backup.Stage(_dir, conflictingPaths));
        Assert.True(Backup.ApplyStaged(_dir));
        Assert.Equal("complete", File.ReadAllText(Path.Combine(_dir, "settings.json")));
        Assert.False(Directory.Exists(Path.Combine(_dir, "extensions")));
    }

    [Fact]
    public void Startup_recovers_an_interrupted_replacement_and_removes_new_optional_data()
    {
        var transaction = Directory.CreateDirectory(Path.Combine(_dir, "restore-transaction")).FullName;
        var original = Directory.CreateDirectory(Path.Combine(transaction, "old")).FullName;
        File.WriteAllText(Path.Combine(original, "settings.json"), "original");
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "partially restored");
        File.WriteAllText(Path.Combine(_dir, "wallpaper.jpg"), "partially added");
        File.WriteAllText(Path.Combine(transaction, "plan.json"), """
            [{"Name":"settings.json","Existed":true},{"Name":"wallpaper.jpg","Existed":false}]
            """);
        Assert.False(Backup.ApplyStaged(_dir));
        Assert.Equal("original", File.ReadAllText(Path.Combine(_dir, "settings.json")));
        Assert.False(File.Exists(Path.Combine(_dir, "wallpaper.jpg")));
        Assert.False(Directory.Exists(transaction));
    }

    [Fact]
    public void A_committed_restore_is_kept_when_cleanup_was_interrupted()
    {
        var transaction = Directory.CreateDirectory(Path.Combine(_dir, "restore-transaction")).FullName;
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "restored");
        File.WriteAllText(Path.Combine(transaction, "committed"), "complete");
        Assert.True(Backup.ApplyStaged(_dir));
        Assert.Equal("restored", File.ReadAllText(Path.Combine(_dir, "settings.json")));
        Assert.False(Directory.Exists(transaction));
    }

    [Fact]
    public void Nested_empty_extension_directories_round_trip()
    {
        var source = Directory.CreateDirectory(Path.Combine(_dir, "source")).FullName;
        Directory.CreateDirectory(Path.Combine(source, "extensions", "empty", "nested"));
        var target = Directory.CreateDirectory(Path.Combine(_dir, "target")).FullName;
        Backup.Stage(target, Backup.Open(Backup.Seal(Backup.Collect(source), "test key"), "test key")!);
        Assert.True(Backup.ApplyStaged(target));
        Assert.True(Directory.Exists(Path.Combine(target, "extensions", "empty", "nested")));
    }

    [Fact]
    public void Altered_prepared_files_are_rejected_before_the_profile_is_touched()
    {
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "original");
        Backup.Stage(_dir, new Dictionary<string, byte[]> { ["settings.json"] = "restored"u8.ToArray() });
        var generation = File.ReadAllText(Path.Combine(_dir, "restore", "current"));
        File.WriteAllText(Path.Combine(_dir, "restore", generation, "settings.json"), "damaged");
        Assert.Throws<InvalidDataException>(() => Backup.ApplyStaged(_dir));
        Assert.Equal("original", File.ReadAllText(Path.Combine(_dir, "settings.json")));
    }
}
