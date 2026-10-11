using Browser.Core;

public class StartWallpapersTests
{
    [Fact]
    public void Catalogue_is_consistent_and_its_tint_keeps_white_text_readable()
    {
        Assert.Equal(StartWallpapers.All.Count, StartWallpapers.All.Select(w => w.Id).Distinct().Count());
        foreach (var photo in StartWallpapers.All)
        {
            Assert.StartsWith("https://picsum.photos/id/", photo.ImageUrl);
            Assert.StartsWith("https://unsplash.com/photos/", photo.Page);
            Assert.NotEmpty(photo.Author);
            var (r, g, b) = photo.Tint;
            Assert.True(0.299 * r + 0.587 * g + 0.114 * b <= 79, $"photo {photo.Id}");
        }
    }

    [Fact]
    public void Settings_choose_sols_image_a_fixed_photo_or_the_photo_of_the_day()
    {
        var day = new DateOnly(2026, 10, 11);
        var settings = new Settings();
        Assert.Null(StartWallpapers.Selected(settings, day)); // fond coupé
        settings.StartWallpaper = true;
        Assert.Null(StartWallpapers.Selected(settings, day)); // image de Sol
        settings.StartWallpaperPhoto = 830;
        Assert.Equal(830, StartWallpapers.Selected(settings, day)!.Id);
        settings.StartWallpaperPhoto = 999_999; // photo retirée du catalogue : retour à l'image de Sol
        Assert.Null(StartWallpapers.Selected(settings, day));
        settings.StartWallpaperDaily = true;
        var today = StartWallpapers.Selected(settings, day)!;
        Assert.Equal(today, StartWallpapers.Selected(settings, day));
        Assert.NotEqual(today, StartWallpapers.Selected(settings, day.AddDays(1)));
    }
}
