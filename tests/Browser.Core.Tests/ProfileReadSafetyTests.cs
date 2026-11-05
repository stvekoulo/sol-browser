using Browser.Core;

public sealed class ProfileReadSafetyTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sol-profile-read-").FullName;
    private string Path_ => Path.Combine(_dir, "settings.json");

    [Fact]
    public void A_locked_primary_must_not_be_replaced_by_defaults()
    {
        Assert.True(JsonFileStore.Save(Path_, new Settings { Shortcuts = [new() { Name = "Personal", Url = "https://saved.test" }] }));
        using var locked = File.Open(Path_, FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.ThrowsAny<IOException>(() => JsonFileStore.Load<Settings>(Path_, requireReadable: true));
    }

    [Fact]
    public void A_locked_primary_must_not_fall_back_to_an_older_backup()
    {
        Assert.True(JsonFileStore.Save(Path_, new Settings { ProfileName = "Old" }));
        Assert.True(JsonFileStore.Save(Path_, new Settings { ProfileName = "Current" }));
        using var locked = File.Open(Path_, FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.ThrowsAny<IOException>(() => JsonFileStore.Load<Settings>(Path_, requireReadable: true));
    }

    [Fact]
    public void An_unrecoverable_profile_is_preserved_and_never_loaded_as_empty()
    {
        File.WriteAllText(Path_, "{ broken");
        File.WriteAllText(Path_ + ".bak", "null");
        Assert.Throws<InvalidDataException>(() => JsonFileStore.Load<Settings>(Path_, requireReadable: true));
        Assert.Equal("{ broken", File.ReadAllText(Path_));
        Assert.Equal("null", File.ReadAllText(Path_ + ".bak"));
    }

    [Fact]
    public void A_previous_quarantine_must_not_be_treated_as_a_new_profile()
    {
        File.WriteAllText(Path_ + ".corrupt", "{ broken");
        Assert.Throws<InvalidDataException>(() => JsonFileStore.Load<Session>(Path_, requireReadable: true));
        Assert.False(File.Exists(Path_));
    }

    [Fact]
    public void A_new_profile_still_starts_with_defaults()
    {
        Assert.True(JsonFileStore.Load<Settings>(Path_, requireReadable: true).FanEnabled);
        Assert.Empty(JsonFileStore.Load<Session>(Path_, requireReadable: true).Urls);
    }

    [Fact]
    public void A_valid_backup_recovers_a_corrupt_primary_without_erasing_it()
    {
        File.WriteAllText(Path_, "{ broken");
        Assert.True(JsonFileStore.Save(Path_ + ".bak", new Settings { ProfileName = "Recovered" }));
        Assert.Equal("Recovered", JsonFileStore.Load<Settings>(Path_, requireReadable: true).ProfileName);
        Assert.Equal("{ broken", File.ReadAllText(Path_));
    }

    public void Dispose() => Directory.Delete(_dir, true);
}
