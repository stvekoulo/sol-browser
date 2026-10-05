using System.Net;

namespace Browser.Core;

/// <summary>Pages affichées par le navigateur quand un site ne peut pas être chargé ou qu'une page plante.</summary>
public static class ErrorPages
{
    /// <summary>Erreur réseau, certificat ou moteur ; les erreurs inconnues restent génériques.</summary>
    public static (string Title, string Text) Describe(string kind, string lang = "fr") => lang == "fr"
        ? kind switch
        {
            "dns" => ("Site introuvable", "L'adresse n'existe pas, ou elle contient une faute de frappe."),
            "offline" => ("Connexion Internet interrompue", "La connexion réseau a été perdue. Vérifiez votre connexion, puis réessayez."),
            "connect" => ("Site inaccessible", "La connexion à ce serveur n'a pas pu être établie. Il peut être indisponible ou refuser la connexion."),
            "reset" => ("Connexion interrompue", "La connexion a été interrompue avant la fin du chargement, par le serveur ou un équipement réseau. Réessayez dans un instant."),
            "response" => ("Réponse du serveur invalide", "Le serveur a envoyé une réponse que le navigateur ne peut pas traiter."),
            "redirect" => ("Redirection impossible", "Le site redirige vers une adresse qui ne peut pas être chargée, ou effectue trop de redirections."),
            "proxy" => ("Authentification du proxy requise", "Le proxy de ce réseau demande un identifiant et un mot de passe. Vérifiez sa configuration dans Windows."),
            "timeout" => ("Le site met trop de temps à répondre", "Il est peut-être surchargé. Réessayez dans un instant."),
            "certificate" => ("Connexion non sécurisée", "Ce site ne prouve pas son identité : quelqu'un pourrait intercepter ce que vous y envoyez. Le navigateur ne l'ouvre pas."),
            "crash" => ("La page a cessé de fonctionner", "Elle a planté. Les autres onglets ne sont pas touchés."),
            _ => ("Cette page ne peut pas être affichée", "Une erreur s'est produite pendant le chargement."),
        }
        : kind switch
        {
            "dns" => ("Site not found", "This address does not exist, or it contains a typo."),
            "offline" => ("Internet connection lost", "The network connection was lost. Check your connection, then try again."),
            "connect" => ("Site unreachable", "A connection to this server could not be established. It may be unavailable or refusing the connection."),
            "reset" => ("Connection interrupted", "The connection was interrupted before loading finished, by the server or network equipment. Try again in a moment."),
            "response" => ("Invalid server response", "The server sent a response that the browser cannot process."),
            "redirect" => ("Unable to redirect", "The site redirects to an address that cannot be loaded, or makes too many redirects."),
            "proxy" => ("Proxy authentication required", "This network's proxy requires a username and password. Check its configuration in Windows."),
            "timeout" => ("The site is taking too long to respond", "It may be overloaded. Try again in a moment."),
            "certificate" => ("Connection not secure", "This site cannot prove its identity: someone could intercept what you send to it. The browser will not open it."),
            "crash" => ("This page stopped working", "It crashed. Your other tabs are not affected."),
            _ => ("This page cannot be displayed", "An error occurred while loading."),
        };

    /// <summary>
    /// Page d'erreur autonome (aucun script, aucune ressource externe). L'adresse vient de l'extérieur : elle est
    /// toujours échappée, et le lien « Réessayer » n'existe que pour une adresse web.
    /// </summary>
    public static string Html(string url, string kind, string lang = "fr", string? status = null)
    {
        var (title, text) = Describe(kind, lang);
        var shown = WebUtility.HtmlEncode(url);
        var detail = status is null ? "" : $"<code>{WebUtility.HtmlEncode(status)}</code>";
        var isWeb = Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http";
        // Un site dont l'identité n'est pas prouvée : pas de bouton pour passer outre.
        // Serveur de développement pas encore démarré : la page se retente toute seule (balise meta, aucun script).
        var local = isWeb && uri!.IsLoopback && kind is not ("certificate" or "crash");
        var refresh = local ? $"""<meta http-equiv="refresh" content="3;url={shown}">""" : "";
        var waiting = local ? $"<p>{(lang == "fr" ? "Serveur local : nouvel essai automatique toutes les 3 secondes." : "Local server: retrying automatically every 3 seconds.")}</p>" : "";
        var retry = isWeb && kind != "certificate" ? $"""<a class="retry" href="{shown}">{(lang == "fr" ? "Réessayer" : "Try again")}</a>""" : "";
        return $$"""
            <!doctype html>
            <html lang="{{lang}}" data-nav-error>
            <head>
            <meta charset="utf-8">
            <meta name="color-scheme" content="light dark">
            {{refresh}}
            <title>{{WebUtility.HtmlEncode(title)}}</title>
            <style>
              /* Une page ordinaire : du texte sur le fond de la page, sans cadre. */
              html { font: 16px/1.6 Inter, "Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif; background: #ffffff; color: #1d1d1f; }
              body { margin: 0; }
              main { max-width: 640px; margin: 0 auto; padding: 14vh 32px 48px; }
              h1 { margin: 0 0 12px; font-size: 30px; font-weight: 650; line-height: 1.2; letter-spacing: -0.01em; }
              p { margin: 0 0 20px; font-size: 17px; color: #515154; }
              code { display: block; margin: 0 0 28px; font: 14px/1.5 ui-monospace, Consolas, monospace; color: #6e6e73; overflow-wrap: anywhere; }
              .retry { color: #0066cc; font-weight: 500; text-decoration: none; }
              .retry:hover { text-decoration: underline; }
              .retry:focus-visible { outline: 2px solid #0066cc; outline-offset: 3px; border-radius: 3px; }
              @media (prefers-color-scheme: dark) {
                html { background: #1c1c1e; color: #f5f5f7; }
                p { color: #a1a1a6; }
                code { color: #8e8e93; }
                .retry { color: #4da3ff; }
                .retry:focus-visible { outline-color: #4da3ff; }
              }
            </style>
            </head>
            <body>
            <main>
              <h1>{{WebUtility.HtmlEncode(title)}}</h1>
              <p>{{WebUtility.HtmlEncode(text)}}</p>
              {{waiting}}
              <code>{{shown}}</code>
              {{detail}}
              {{retry}}
            </main>
            </body>
            </html>
            """;
    }
}
