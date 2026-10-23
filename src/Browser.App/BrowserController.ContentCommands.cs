using System.Runtime.InteropServices;
using System.Text.Json;
using Browser.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Browser.App;

public sealed partial class BrowserController
{
    private bool ExecuteCommands0(Command c)
    {
        if (c.Type.StartsWith("notes.", StringComparison.Ordinal)) return ExecuteNotes(c);
        var id = c.Id ?? "";
        switch (c.Type)
        {
            case "lan.open": ShowLanPreview(id); return true;
            case "device.sync": _pages.ToggleDeviceSync(id); return true;
            case "device.theme": _pages.ToggleDeviceDark(id); return true;
            case "ai.connect": if (!PrivateActive) _ = _chatGpt.SignInAsync(); return true;
            case "ai.connect.cancel": _chatGpt.CancelSignIn(); return true;
            case "ai.disconnect": CancelAi(); CancelChat(); CancelCategories(); _chat.Clear(); PublishChat(); _ = _chatGpt.SignOutAsync(); return true;
            case "ai.cancel": CancelAi(); return true;
            case "ai.approve": AnswerApproval(c); return true;
            case "ai.categorize": _ = CategorizeAsync(true); return true;
            case "ai.open": _ = OpenPageAiAsync(id, null, "summary"); return true; // bouton Résumer : réponse immédiate
            case "ai.run": _ = RunTextAiAsync(c); return true;
            case "ai.apply": _ = ApplyTextAiAsync(); return true;
            case "ai.copy": CopyAi(); return true;
            case "ai.note": NoteAi(); return true;
            case "palette.open": OpenPalette(); return true;
            case "palette.pick":
                if (_paletteCommands.TryGetValue(id, out var chosen))
                { HidePopup(); Execute(chosen); }
                return true;
            case "reader.toggle": _ = ToggleReaderAsync(id); break;
            case "video.open": OpenVideos(id); return true;
            case "video.inspect": _ = InspectVideoAsync(id, c.Query!); return true;
            case "video.download": _ = DownloadVideoAsync(id, c.Query!, c.Index!.Value); return true;
            case "video.collection": _ = InspectVideoCollectionAsync(id, c.Query!, c.Index!.Value); return true;
            case "video.batch": _ = DownloadVideoCollectionAsync(id, c.Query!, c.Input!, c.Index!.Value); return true;
            case "webapp.info":
                if (_pages.WebAppOf(id) is { } app)
                    Notice(app.Name, T($"Ce site propose une application web progressive (PWA).\nAdresse de lancement : {app.StartUrl}",
                        $"This site offers a progressive web app (PWA).\nLaunch address: {app.StartUrl}"));
                return true;
            case "design.toggle": _ = ToggleDesignAsync(id); return true;
            case "reader.listen" or "reader.stop": _ = ReaderSpeechAsync(id, c.Type == "reader.stop"); return true;
            case "bookmark.toggle":
                if (_tabs.Tabs.FirstOrDefault(t => t.Id == id) is { } mark && IsWeb(mark.Url) && !mark.Url.StartsWith("https://start.sol/", StringComparison.OrdinalIgnoreCase))
                { if (_db.IsBookmarked(mark.Url)) _db.RemoveBookmark(mark.Url); else _db.AddBookmark(mark.Url, mark.Title); }
                break;
            case "translation.open":
                _ = OpenTranslationAsync(id, c.X!.Value, c.Y!.Value);
                return true;
            case "translation.run":
                if (_translationSource is { } requested && requested.Id == id
                    && _tabs.Tabs.FirstOrDefault(t => t.Id == id) is { } sourceTab
                    && (sourceTab.Url == requested.Url || PageTranslation.IsGooglePage(sourceTab.Url) && PageTranslation.IsGooglePage(requested.Url))
                    && PageTranslation.GoogleUrl(PageTranslation.IsGooglePage(sourceTab.Url) ? _translationOriginals.GetValueOrDefault(id, sourceTab.Url) : sourceTab.Url, c.Query!) is { } translatedUrl)
                {
                    if (!PageTranslation.IsGooglePage(sourceTab.Url)) _translationOriginals[id] = sourceTab.Url;
                    HidePopup(); _pages.Navigate(id, translatedUrl); Refresh();
                }
                return true;
            case "translation.original":
                if (_translationSource is { } page && page.Id == id && _translationOriginals.TryGetValue(id, out var originalUrl)
                    && _tabs.Tabs.FirstOrDefault(t => t.Id == id) is { } originalTab
                    && (originalTab.Url == page.Url || PageTranslation.IsGooglePage(originalTab.Url)))
                { HidePopup(); _pages.Navigate(id, originalUrl); Refresh(); }
                return true;
            // Vue posée sur la page : ces commandes ne changent pas l'état des onglets.
            case "suggest": Suggest(id, c.Query!, c.X!.Value, c.Y!.Value, c.Width!.Value); return true;
            case "library.open": OpenLibrary(c.X!.Value, c.Y!.Value); return true;
            case "history.open": OpenLibrary(c.X!.Value, c.Y!.Value, history: true); return true;
            case "library.search":
                if (_popup.IsOpen && (_panel == "library" || _panelNext == "library")) _popup.Post(LibraryJson(c.Query!, c.Index!.Value));
                return true;
            case "downloads.open": OpenDownloads(c.X!.Value, c.Y!.Value); return true;
            case "download.cancel" or "download.pause" or "download.resume": ControlDownload(id,c.Type); return true;
            case "download.open" or "download.reveal":
                if (FindDownload(id) is { State: Microsoft.Web.WebView2.Core.CoreWebView2DownloadState.Completed } done && File.Exists(done.ResultFilePath))
                {
                    // Ouvrir un fichier téléchargé l'exécute avec son application : uniquement sur clic de l'utilisateur.
                    var start = c.Type == "download.open"
                        ? new System.Diagnostics.ProcessStartInfo(done.ResultFilePath) { UseShellExecute = true }
                        : new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{done.ResultFilePath}\"");
                    try { System.Diagnostics.Process.Start(start); }
                    catch (System.ComponentModel.Win32Exception) { /* aucune application associée */ }
                }
                HidePopup();
                return true;
            case "popup.size":
                _popup.SetHeight(c.Height!.Value);
                if (_panelNext is not null) (_panel, _panelNext) = (_panelNext, null); // le panneau demandé est maintenant affiché
                return true;
            case "popup.close":
                if (!_suggesting) _libraryClosedAt = DateTime.UtcNow;
                HidePopup();
                return true;
            case "suggest.select":
                if (_suggesting) _popup.Post(JsonSerializer.Serialize(new { select = c.Index }));
                return true;
            case "menu.pick": PickMenuItem(c.Index!.Value); return true;
            case "settings.open": OpenSettings(c.X!.Value, c.Y!.Value); return true;
            case "spelling.languages":
                _ = Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:regionlanguage"));
                return true;
            default: return false;
        }
        HidePopup(); Refresh();
        return true;
    }
}
