using System.IO.Pipes;
using System.Runtime.InteropServices;
using Browser.Core;
using Microsoft.Win32;

namespace Browser.App;

/// <summary>
/// Sol vu par Windows comme un navigateur : il figure dans « Ouvrir avec » pour les pages et les PDF, et parmi les
/// applications par défaut pour les liens. Tout est écrit pour le compte de l'utilisateur seulement (aucun droit
/// d'administrateur), par la copie installée, et retiré à la désinstallation. Windows ne laisse pas une application se
/// désigner elle-même navigateur par défaut : c'est l'utilisateur qui la choisit dans ses paramètres.
/// </summary>
public static class WindowsBrowser
{
    private const string Client = @"Software\Clients\StartMenuInternet\Sol";
    private const string Document = "SolBrowserDocument", Link = "SolBrowserURL";
    private static readonly string[] Files = [".htm", ".html", ".shtml", ".xhtml", ".pdf", ".svg", ".webp"];
    private static readonly string[] Schemes = ["http", "https"];

    public static void Register(string exe)
    {
        try
        {
            using var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes");
            foreach (var (id, name) in new[] { (Document, "Sol document"), (Link, "Sol link") })
            {
                using var kind = classes.CreateSubKey(id);
                kind.SetValue("", name);
                if (id == Link) kind.SetValue("URL Protocol", "");
                using (var icon = kind.CreateSubKey("DefaultIcon")) icon.SetValue("", $"\"{exe}\",0");
                using var command = kind.CreateSubKey(@"shell\open\command");
                command.SetValue("", $"\"{exe}\" \"%1\"");
            }
            // « Ouvrir avec » de chaque type de fichier.
            foreach (var extension in Files)
            {
                using var with = classes.CreateSubKey($@"{extension}\OpenWithProgids");
                with.SetValue(Document, Array.Empty<byte>(), RegistryValueKind.None);
            }

            using (var client = Registry.CurrentUser.CreateSubKey(Client))
            {
                client.SetValue("", "Sol");
                using (var icon = client.CreateSubKey("DefaultIcon")) icon.SetValue("", $"\"{exe}\",0");
                using (var command = client.CreateSubKey(@"shell\open\command")) command.SetValue("", $"\"{exe}\"");
                using var capabilities = client.CreateSubKey("Capabilities");
                capabilities.SetValue("ApplicationName", "Sol");
                capabilities.SetValue("ApplicationDescription", Language.T("Navigateur web léger pour Windows.", "A light web browser for Windows."));
                capabilities.SetValue("ApplicationIcon", $"\"{exe}\",0");
                using (var files = capabilities.CreateSubKey("FileAssociations"))
                    foreach (var extension in Files) files.SetValue(extension, Document);
                using (var links = capabilities.CreateSubKey("URLAssociations"))
                    foreach (var scheme in Schemes) links.SetValue(scheme, Link);
                using var menu = capabilities.CreateSubKey("StartMenu");
                menu.SetValue("StartMenuInternet", "Sol");
            }
            using (var registered = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
                registered.SetValue("Sol", Client + @"\Capabilities");
            SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, 0, 0);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException) { DebugLog.Write($"enregistrement : {e.Message}"); }
    }

    public static void Unregister()
    {
        try
        {
            using var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes");
            classes.DeleteSubKeyTree(Document, throwOnMissingSubKey: false);
            classes.DeleteSubKeyTree(Link, throwOnMissingSubKey: false);
            foreach (var extension in Files)
            {
                using var with = classes.OpenSubKey($@"{extension}\OpenWithProgids", writable: true);
                with?.DeleteValue(Document, throwOnMissingValue: false);
            }
            Registry.CurrentUser.DeleteSubKeyTree(Client, throwOnMissingSubKey: false);
            using (var registered = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", writable: true))
                registered?.DeleteValue("Sol", throwOnMissingValue: false);
            SHChangeNotify(0x08000000, 0, 0, 0);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException) { }
    }

    [DllImport("shell32.dll")] private static extern void SHChangeNotify(int eventId, uint flags, nint item1, nint item2);

    // ---------- Ce que Windows demande d'ouvrir

    /// <summary>Adresses à ouvrir, tirées des arguments du lancement : un lien web, ou un fichier qui existe.</summary>
    public static IReadOnlyList<string> Targets(IEnumerable<string> args)
    {
        var targets = new List<string>();
        foreach (var arg in args.Skip(1))
        {
            if (arg.StartsWith("--", StringComparison.Ordinal)) continue;
            if (!Uri.TryCreate(arg, UriKind.Absolute, out var uri)) { if (File.Exists(arg)) targets.Add(new Uri(Path.GetFullPath(arg)).AbsoluteUri); }
            else if (uri.Scheme is "http" or "https") targets.Add(uri.AbsoluteUri);
            // Un fichier, donné par son chemin (lancement par Windows) ou déjà sous forme d'adresse (remis par un autre lancement).
            else if (uri.IsFile && File.Exists(uri.LocalPath)) targets.Add(uri.AbsoluteUri);
        }
        return targets;
    }

    private static string PipeName => $"Sol.Ouvrir.{Environment.UserName}.{AppPaths.Profile}";

    /// <summary>Sol tourne déjà pour ce profil : les adresses lui sont remises, il les ouvre dans de nouveaux onglets.</summary>
    public static void SendToRunning(IReadOnlyList<string> targets)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            pipe.Connect(2000);
            // Le lancement demandé par l'utilisateur transmet son droit de premier plan à la fenêtre existante.
            if (GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var processId)) AllowSetForegroundWindow(processId);
            using var writer = new StreamWriter(pipe);
            foreach (var target in targets) writer.WriteLine(target);
            if (targets.Count == 0) writer.WriteLine(""); // lancé sans rien à ouvrir : la fenêtre existante passe devant
        }
        catch (Exception e) when (e is IOException or TimeoutException or UnauthorizedAccessException) { }
    }

    /// <summary>Écoute les demandes des lancements suivants. Chaque ligne reçue est une adresse (vide : montrer la fenêtre).</summary>
    public static void Listen(Action<string> open)
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    // Réservé à cet utilisateur : un autre compte de la machine ne peut pas faire ouvrir une page.
                    using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly);
                    pipe.WaitForConnection();
                    using var reader = new StreamReader(pipe);
                    while (reader.ReadLine() is { } line)
                        if (line.Length == 0 || Targets(["", line]).Count == 1) open(line);
                }
                catch (IOException) { Thread.Sleep(500); }
            }
        }) { IsBackground = true, Name = "Sol.Ouvrir" };
        thread.Start();
    }

    [DllImport("kernel32.dll")] private static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint processId);
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint processId);
}
