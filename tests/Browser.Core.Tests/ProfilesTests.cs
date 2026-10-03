using Browser.Core;

public class ProfilesTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("nav-profils").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData(new[] { "navigateur.exe" }, "defaut")]
    [InlineData(new[] { "navigateur.exe", "--profile=profil-2" }, "profil-2")]
    [InlineData(new[] { "navigateur.exe", "--profile=travail" }, "travail")]
    public void FromArgs_reads_the_requested_profile(string[] args, string expected) =>
        Assert.Equal(expected, Profiles.FromArgs(args));

    [Theory]
    [InlineData("--profile=../../Windows")]
    [InlineData("--profile=C:\\Users")]
    [InlineData("--profile=a/b")]
    [InlineData("--profile=")]
    [InlineData("--profile=Profil 2")]
    [InlineData("--profile=-x")]
    [InlineData("--profile=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void FromArgs_falls_back_to_default_for_names_that_are_not_plain_folder_names(string arg) =>
        Assert.Equal(Profiles.Default, Profiles.FromArgs(["navigateur.exe", arg]));

    [Fact]
    public void DataDir_keeps_the_default_profile_at_the_root_and_others_apart()
    {
        Assert.Equal(_dir, Profiles.DataDir(_dir, Profiles.Default));
        Assert.Equal(Path.Combine(_dir, "profiles", "profil-2"), Profiles.DataDir(_dir, "profil-2"));
    }

    [Fact]
    public void List_starts_with_default_and_ignores_stray_folders()
    {
        Assert.Equal(new[] { "defaut" }, Profiles.List(_dir));
        foreach (var name in new[] { "profil-3", "profil-2", "Pas Valide", "defaut" })
            Directory.CreateDirectory(Path.Combine(_dir, "profiles", name));
        Assert.Equal(new[] { "defaut", "profil-2", "profil-3" }, Profiles.List(_dir));
    }

    [Fact]
    public void Create_derives_a_folder_name_and_keeps_the_typed_name()
    {
        var id = Profiles.Create(_dir, "  Travail & Études  ");
        Assert.Equal("travail-etudes", id);
        Assert.True(Directory.Exists(Path.Combine(_dir, "profiles", id)));
        Assert.Equal("Travail & Études", Profiles.NameOf(_dir, id));
        Assert.Equal(new[] { "defaut", "travail-etudes" }, Profiles.List(_dir));
    }

    [Fact]
    public void Create_never_reuses_an_existing_or_reserved_folder()
    {
        Assert.Equal("perso", Profiles.Create(_dir, "Perso"));
        Assert.Equal("perso-2", Profiles.Create(_dir, "perso"));
        Assert.Equal("defaut-2", Profiles.Create(_dir, "Défaut"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("../..")]
    [InlineData("日本語")]
    public void Create_copes_with_names_that_give_no_usable_folder_name(string name)
    {
        var id = Profiles.Create(_dir, name);
        Assert.True(Profiles.IsValid(id));
        Assert.StartsWith(Path.Combine(_dir, "profiles"), Path.GetFullPath(Profiles.DataDir(_dir, id)));
        Assert.NotEqual("", Profiles.NameOf(_dir, id));
    }

    [Fact]
    public void NameOf_falls_back_when_no_name_was_stored()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "profiles", "ancien"));
        Assert.Equal("ancien", Profiles.NameOf(_dir, "ancien"));
        Assert.Equal("Défaut", Profiles.NameOf(_dir, Profiles.Default));
    }

    [Fact]
    public void Delete_removes_a_created_profile_with_its_data()
    {
        var id = Profiles.Create(_dir, "Jetable");
        File.WriteAllText(Path.Combine(Profiles.DataDir(_dir, id), "session.json"), "{}");
        Assert.True(Profiles.Delete(_dir, id));
        Assert.False(Directory.Exists(Profiles.DataDir(_dir, id)));
        Assert.False(Profiles.Delete(_dir, id));
    }

    [Theory]
    [InlineData("defaut")]
    [InlineData("..")]
    [InlineData("../..")]
    [InlineData("inconnu")]
    public void Delete_refuses_the_default_profile_and_anything_that_is_not_a_profile(string id)
    {
        File.WriteAllText(Path.Combine(_dir, "browser.db"), "données du profil par défaut");
        Assert.False(Profiles.Delete(_dir, id));
        Assert.True(File.Exists(Path.Combine(_dir, "browser.db")));
    }
}
