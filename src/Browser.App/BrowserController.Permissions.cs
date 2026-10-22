using System.Text.Json;
using Browser.Core;
using Microsoft.Web.WebView2.Core;

namespace Browser.App;

public sealed partial class BrowserController
{
    private readonly PermissionStore _defaultPermissions = new(AppPaths.Permissions);
    private readonly PermissionStore _privatePermissions = new(null);
    private readonly Dictionary<string, PermissionStore> _sessionPermissions = new();
    private PermissionStore PermissionFor(string session) => session.Length == 0 ? _defaultPermissions
        : _sessionPermissions.TryGetValue(session, out var store) ? store
        : _sessionPermissions[session] = new(PermissionStore.FileForProfile(AppPaths.DataDir, "sol-" + session));
    private PermissionStore _permissions => PrivateActive ? _privatePermissions : PermissionFor(_tabs.Tabs.FirstOrDefault(t => t.Id == _tabs.ActiveId)?.SessionId ?? "");

    private sealed record PermissionRequest(CoreWebView2PermissionRequestedEventArgs Args, Windows.Foundation.Deferral Deferral,
        string Origin, CoreWebView2 Core, string? PageOrigin, PermissionStore Store);
    private PermissionRequest? _asking;
    private readonly Queue<PermissionRequest> _permissionQueue = new();

    private static string PermissionLabel(CoreWebView2PermissionKind kind) => kind switch
    {
        CoreWebView2PermissionKind.Microphone => T("utiliser votre micro", "use your microphone"),
        CoreWebView2PermissionKind.Camera => T("utiliser votre caméra", "use your camera"),
        CoreWebView2PermissionKind.Geolocation => T("connaître votre position", "know your location"),
        CoreWebView2PermissionKind.Notifications => T("vous envoyer des notifications", "send you notifications"),
        CoreWebView2PermissionKind.ClipboardRead => T("lire le contenu du presse-papiers", "read the clipboard"),
        CoreWebView2PermissionKind.MultipleAutomaticDownloads => T("télécharger plusieurs fichiers à la suite", "download several files in a row"),
        CoreWebView2PermissionKind.FileReadWrite => T("lire et modifier des fichiers de cet ordinateur", "read and change files on this computer"),
        CoreWebView2PermissionKind.OtherSensors => T("lire les capteurs de mouvement", "read motion sensors"),
        _ => T($"utiliser une fonction sensible ({kind})", $"use a sensitive feature ({kind})"),
    };

    private void OnPermissionRequested(CoreWebView2 core, CoreWebView2PermissionRequestedEventArgs args)
    {
        // Les autorisations média et non sensibles (autoplay, polices, veille) sont accordées d'office.
        if (args.PermissionKind is CoreWebView2PermissionKind.Autoplay or CoreWebView2PermissionKind.LocalFonts || (int)args.PermissionKind == 13)
        { args.State = CoreWebView2PermissionState.Allow; return; }
        args.SavesInProfile = false;
        var origin = PermissionStore.OriginOf(args.Uri);
        var tab = _tabs.Tabs.FirstOrDefault(t => _pages.CoreOf(t.Id) == core);
        if (origin is null || tab is null) { args.State = CoreWebView2PermissionState.Deny; return; }
        var store = tab.Private || AppPaths.Private ? _privatePermissions : PermissionFor(tab.SessionId);
        if (store.Get(origin, args.PermissionKind.ToString()) is { } known)
        {
            args.SavesInProfile = !AppPaths.Private && !tab.Private;
            args.State = known ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
            return;
        }
        if (_tabs.ActiveId != tab.Id || _permissionQueue.Count >= 8)
        { args.State = CoreWebView2PermissionState.Deny; return; }
        _permissionQueue.Enqueue(new(args, args.GetDeferral(), origin, core, PermissionStore.OriginOf(core.Source), store));
        if (!_window.DispatcherQueue.TryEnqueue(ShowNextPermission)) DismissPermissions();
    }

    private bool CurrentPermission(PermissionRequest request) => _tabs.ActiveId is { } active
        && _pages.CoreOf(active) == request.Core && PermissionStore.OriginOf(request.Core.Source) == request.PageOrigin;

    private void ShowNextPermission()
    {
        if (_asking is not null || _closingWindow) return;
        while (_permissionQueue.TryDequeue(out var request))
        {
            if (!CurrentPermission(request)) { RefusePermission(request); continue; }
            HidePopup(cancelPermissions: false);
            _asking = request;
            _popup.ShowCentered(360, JsonSerializer.Serialize(new { popup = "permission", site = new Uri(request.Origin).Host,
                what = PermissionLabel(request.Args.PermissionKind), secure = request.Origin.StartsWith("https://", StringComparison.Ordinal) }), top: 70);
            return;
        }
    }

    private void AnswerPermission(bool allow)
    {
        if (_asking is not { } request) return;
        _asking = null;
        HidePopup(cancelPermissions: false);
        try
        {
            if (!CurrentPermission(request)) { request.Args.State = CoreWebView2PermissionState.Deny; return; }
            if (!request.Store.Set(request.Origin, request.Args.PermissionKind.ToString(), allow))
                throw new IOException("Permission could not be saved");
            request.Args.SavesInProfile = !AppPaths.Private && !PrivateBrowsing.IsProfile(request.Core.Profile.ProfileName);
            request.Args.State = allow ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            request.Args.State = CoreWebView2PermissionState.Deny;
            DebugLog.Write("permission.save.failed " + error.GetType().Name);
        }
        finally { request.Deferral.Complete(); _window.DispatcherQueue.TryEnqueue(ShowNextPermission); }
    }

    private static void RefusePermission(PermissionRequest request)
    {
        request.Args.SavesInProfile = false;
        request.Args.State = CoreWebView2PermissionState.Deny;
        request.Deferral.Complete();
    }
    private void DismissPermissions()
    {
        var pending = _permissionQueue.ToList();
        _permissionQueue.Clear();
        if (_asking is { } question) { _asking = null; pending.Insert(0, question); }
        foreach (var request in pending) RefusePermission(request);
    }

    private async Task ForgetPermissionsAsync(string? origin)
    {
        if (EngineProfile is not { } profile || origin is not null && PermissionStore.OriginOf(origin) != origin) return;
        try
        {
            if (!(origin is null ? _permissions.Clear() : _permissions.Forget(origin))) throw new IOException("Permission reset could not be saved");
            await NativePermissions.ResetAsync(profile, origin);
            if (_settingsOpen) _popup.Post(SettingsJson());
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        { Notice(T("Autorisations non réinitialisées", "Permissions were not reset"), error.Message); }
    }
}
