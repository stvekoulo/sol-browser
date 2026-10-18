using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Browser.Core;

namespace Browser.App;

public sealed partial class BrowserController
{
    private static string CustomWallpaperFile => Path.Combine(AppPaths.DataDir, "wallpaper.jpg");
    private bool _pickingWallpaper;

    private async Task PickWallpaperAsync()
    {
        if (_pickingWallpaper) return;
        _pickingWallpaper = true;
        HidePopup(); // Le sélecteur natif s’ouvre après le retour du callback WebView2.
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
            foreach (var extension in new[] { ".jpg", ".jpeg", ".png", ".webp", ".bmp" }) picker.FileTypeFilter.Add(extension);
            OwnPicker(picker);
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;
            await Task.Run(() => SavePersonalWallpaperAsync(file));
            if (_closingWindow) return;
            _wallpaperLoaded = null;
            _settings.StartWallpaper = true;
            _settings.StartWallpaperDaily = false;
            _settings.StartWallpaperCustom = true;
            WallpaperChanged();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            Notice(T("Photo personnelle", "Personal photo"), T("Cette image ne peut pas être utilisée. Choisissez une image JPEG, PNG, WebP ou BMP de moins de 30 Mo.",
                "This image could not be used. Choose a JPEG, PNG, WebP or BMP image under 30 MB."));
        }
        finally { _pickingWallpaper = false; }
    }

    // Copie locale redimensionnée : le fichier original reste intact et aucune photo n'est envoyée sur Internet.
    private static async Task SavePersonalWallpaperAsync(StorageFile file)
    {
        using var input = await file.OpenReadAsync();
        if (input.Size is 0 or > 30_000_000) throw new IOException("Image size");
        var decoder = await BitmapDecoder.CreateAsync(input);
        if ((ulong)decoder.PixelWidth * decoder.PixelHeight > 80_000_000) throw new IOException("Image dimensions");
        var factor = Math.Min(1d, 2560d / Math.Max(decoder.PixelWidth, decoder.PixelHeight));
        var transform = new BitmapTransform
        {
            ScaledWidth = Math.Max(1, (uint)(decoder.PixelWidth * factor)),
            ScaledHeight = Math.Max(1, (uint)(decoder.PixelHeight * factor)),
            InterpolationMode = BitmapInterpolationMode.Fant,
        };
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, transform,
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);
        using var output = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, output);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync();
        var temporary = CustomWallpaperFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            output.Seek(0);
            await using (var target = File.Create(temporary)) await output.AsStreamForRead().CopyToAsync(target);
            File.Move(temporary, CustomWallpaperFile, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
