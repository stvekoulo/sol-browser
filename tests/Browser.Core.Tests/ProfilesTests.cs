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
    public void NextName_takes_the_first_free_number()
    {
        Assert.Equal("profil-2", Profiles.NextName(["defaut"]));
        Assert.Equal("profil-3", Profiles.NextName(["defaut", "profil-2", "profil-4"]));
    }

    [Theory]
    [InlineData("defaut", "Défaut")]
    [InlineData("profil-2", "Profil 2")]
    [InlineData("travail", "travail")]
    public void Display_gives_the_shown_name(string name, string shown) => Assert.Equal(shown, Profiles.Display(name));
}
