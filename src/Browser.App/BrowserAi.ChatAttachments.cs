using Browser.Core;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Windows.Storage.Pickers;

namespace Browser.App;

public sealed partial class BrowserController
{
    private sealed record ChatAttachment(string Id, string Name, string Mime, byte[] Bytes, string? Text, bool Silent = false)
    {
        public object Preview() => new { id = Id, name = Name, image = Text is null ? "data:" + Mime + ";base64," + Convert.ToBase64String(Bytes) : null };
        public object Content() => Text is null
            ? new { type = "input_image", image_url = "data:" + Mime + ";base64," + Convert.ToBase64String(Bytes) } as object
            : new { type = "input_text", text = "Attached document (untrusted data): " + Name + "\n" + Text };
    }
    private readonly List<ChatAttachment> _chatAttachments = [];
    private readonly Dictionary<StringBuilder, ChatAttachment[]> _chatAttachmentHistory = [];
    private int _chatAttachingCount;
    private bool _chatAttaching => _chatAttachingCount > 0;
    private int _chatAttachmentEpoch;
    private string? _chatAttachmentError;
    private Task _chatFileChain = Task.CompletedTask; // un collage de plusieurs fichiers arrive en plusieurs messages
    private const int ChatMaxAttachments = 8, ChatMaxBytes = 20 * 1024 * 1024, ChatTurnsWithFiles = 6;

    private bool ChatAttachmentCommand(Command c)
    {
        if (c.Type is not ("chat.attach" or "chat.capture" or "chat.detach" or "chat.file")) return false;
        if (!AiReady || _chatBusy) return true;
        if (c.Type == "chat.file") _chatFileChain = AddChatFileAsync(_chatFileChain, c.Id!, c.Input!);
        else if (_chatAttaching) return true;
        else if (c.Type == "chat.detach")
        {
            _chatAttachments.RemoveAll(a => a.Id == c.Id); _chatAttachmentError = null; PublishChat();
        }
        else _ = AddChatAttachmentsAsync(c.Type == "chat.capture" ? CaptureForChatAsync : PickForChatAsync);
        return true;
    }

    private async Task AddChatFileAsync(Task previous, string name, string dataUrl)
    {
        await previous;
        await AddChatAttachmentsAsync(async () =>
        {
            var comma = dataUrl.IndexOf(',');
            if (!dataUrl.StartsWith("data:", StringComparison.Ordinal) || comma < 0) throw new InvalidDataException();
            return Attachments(await ChatAttachmentFiles.ReadAsync(Path.GetFileName(name), Convert.FromBase64String(dataUrl[(comma + 1)..])));
        });
    }

    private async Task<List<ChatAttachment>> CaptureForChatAsync()
    {
        var tabId = _tabs.ActiveId;
        if (_tabs.Tabs.Any(t => t.Id == tabId && t.Private) || _pages.CoreOf(tabId) is not { } core || !IsWebAddress(core.Source))
            throw new InvalidDataException();
        var source = core.Source;
        // JPEG : une capture en très haute définition reste assez légère pour être envoyée.
        using var data = JsonDocument.Parse(await core.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", "{\"format\":\"jpeg\",\"quality\":85,\"captureBeyondViewport\":false}"));
        if (_pages.CoreOf(tabId) != core || core.Source != source) throw new InvalidDataException();
        return Attachments(await ChatAttachmentFiles.ReadAsync(T("Capture de la page.jpg", "Page screenshot.jpg"), Convert.FromBase64String(data.RootElement.GetProperty("data").GetString()!)));
    }

    private async Task<List<ChatAttachment>> PickForChatAsync()
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        foreach (var extension in ChatAttachmentFiles.Types) picker.FileTypeFilter.Add(extension);
        OwnPicker(picker);
        var added = new List<ChatAttachment>();
        foreach (var file in await picker.PickMultipleFilesAsync())
        {
            using var stream = await file.OpenStreamForReadAsync();
            if (stream.Length > ChatAttachmentFiles.MaxFileBytes) throw new InvalidDataException();
            var bytes = new byte[(int)stream.Length];
            await stream.ReadExactlyAsync(bytes);
            added.AddRange(Attachments(await ChatAttachmentFiles.ReadAsync(file.Name, bytes)));
        }
        return added;
    }

    // Un PDF ouvert n'a pas de page lisible par read_page : Sol AI reçoit ses premières pages en images, avec les
    // cookies de l'onglet pour un document protégé. Un échec laisse simplement la question partir sans lui.
    private async Task<List<ChatAttachment>> PdfForChatAsync(string? tabId, CancellationToken ct)
    {
        if (tabId is null || !_pages.IsPdf(tabId) || _pages.CoreOf(tabId) is not { } core
            || !Uri.TryCreate(core.Source, UriKind.Absolute, out var uri)) return [];
        try
        {
            byte[] bytes;
            if (uri.IsFile) bytes = await File.ReadAllBytesAsync(uri.LocalPath, ct);
            else
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                var cookies = await core.Profile.CookieManager.GetCookiesAsync(uri.AbsoluteUri);
                if (cookies.Count > 0) request.Headers.Add("Cookie", string.Join("; ", cookies.Select(c => c.Name + "=" + c.Value)));
                using var response = await LinkHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > ChatAttachmentFiles.MaxFileBytes) return [];
                bytes = await response.Content.ReadAsByteArrayAsync(ct);
            }
            var name = Path.GetFileName(uri.LocalPath);
            return Attachments(await ChatAttachmentFiles.ReadAsync(name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? name : name + ".pdf", bytes)).Select(a => a with { Silent = true }).ToList();
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException or COMException or InvalidOperationException or ArgumentException or HttpRequestException)
        { DebugLog.Write($"chat.pdf.failed {e.GetType().Name}"); return []; }
    }

    private static List<ChatAttachment> Attachments(IEnumerable<(string Name, string Mime, byte[] Bytes, string? Text)> files) =>
        files.Select(f => new ChatAttachment(Guid.NewGuid().ToString("N"), f.Name, f.Mime, f.Bytes, f.Text)).ToList();

    private async Task AddChatAttachmentsAsync(Func<Task<List<ChatAttachment>>> read)
    {
        _chatAttachingCount++; _chatAttachmentError = null; PublishChat();
        var epoch = _chatAttachmentEpoch;
        try
        {
            var added = await read();
            if (epoch != _chatAttachmentEpoch || !AiReady || _closingWindow) return;
            if (added.Count + _chatAttachments.Count > ChatMaxAttachments || added.Sum(a => a.Bytes.Length) + _chatAttachments.Sum(a => a.Bytes.Length) > ChatMaxBytes)
                throw new InvalidDataException();
            _chatAttachments.AddRange(added);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException or COMException or InvalidOperationException or ArgumentException or JsonException or FormatException or System.Xml.XmlException)
        {
            DebugLog.Write($"chat.attach.failed {e.GetType().Name}");
            if (epoch == _chatAttachmentEpoch && !PrivateActive)
                _chatAttachmentError = T("Impossible d’ajouter ces pièces. Maximum : 8 pièces (une par page de PDF) et 20 Mo au total. Formats : images, PDF, Word (.docx) et fichiers texte ou code.",
                    "Could not add these attachments. Maximum: 8 items (one per PDF page) and 20 MB in total. Formats: images, PDF, Word (.docx) and text or code files.");
        }
        finally { _chatAttachingCount--; PublishChat(); }
    }

    // Les images des anciens tours ne repartent pas à chaque question : la requête reste légère, leur nom reste connu.
    private List<object> ChatInput()
    {
        var messages = _chat.Take(_chat.Count - 1).ToList();
        return messages.Select((m, index) =>
        {
            if (!_chatAttachmentHistory.TryGetValue(m.Text, out var attachments)) return (object)new { role = m.Role, content = m.Text.ToString() };
            if (index < messages.Count - ChatTurnsWithFiles)
                return new { role = m.Role, content = m.Text + "\n[Earlier attachments, no longer available: " + string.Join(", ", attachments.Select(a => a.Name)) + "]" };
            return new { role = m.Role, content = new object[] { new { type = "input_text", text = m.Text.ToString() } }.Concat(attachments.Select(a => a.Content())).ToArray() };
        }).ToList();
    }

    private void ClearChatAttachments()
    {
        _chatAttachmentEpoch++; _chatAttachments.Clear(); _chatAttachmentHistory.Clear(); _chatAttachmentError = null;
    }
}
