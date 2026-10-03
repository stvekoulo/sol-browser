using System.Net;

namespace Browser.Core;

/// <summary>Pages affichées par le navigateur quand un site ne peut pas être chargé ou qu'une page plante.</summary>
public static class ErrorPages
{
    /// <summary>"dns", "offline", "timeout", "certificate", "crash" ou autre chose (erreur générique).</summary>
    public static (string Title, string Text) Describe(string kind) => kind switch
    {
        "dns" => ("Site introuvable", "L'adresse n'existe pas, ou elle contient une faute de frappe."),
        "offline" => ("Connexion impossible", "Le site ne répond pas, ou cet ordinateur n'est pas connecté à Internet."),
        "timeout" => ("Le site met trop de temps à répondre", "Il est peut-être surchargé. Réessayez dans un instant."),
        "certificate" => ("Connexion non sécurisée", "Ce site ne prouve pas son identité : quelqu'un pourrait intercepter ce que vous y envoyez. Le navigateur ne l'ouvre pas."),
        "crash" => ("La page a cessé de fonctionner", "Elle a planté. Les autres onglets ne sont pas touchés."),
        _ => ("Cette page ne peut pas être affichée", "Une erreur s'est produite pendant le chargement."),
    };

    /// <summary>
    /// Page d'erreur autonome (aucun script, aucune ressource externe). L'adresse vient de l'extérieur : elle est
    /// toujours échappée, et le lien « Réessayer » n'existe que pour une adresse web.
    /// </summary>
    public static string Html(string url, string kind)
    {
        var (title, text) = Describe(kind);
        var shown = WebUtility.HtmlEncode(url);
        var isWeb = Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http";
        // Un site dont l'identité n'est pas prouvée : pas de bouton pour passer outre.
        var retry = isWeb && kind != "certificate" ? $"""<a class="retry" href="{shown}">Réessayer</a>""" : "";
        return $$"""
            <!doctype html>
            <html lang="fr" data-nav-error>
            <head>
            <meta charset="utf-8">
            <meta name="color-scheme" content="light dark">
            <title>{{WebUtility.HtmlEncode(title)}}</title>
            <style>
              html { height: 100%; font: 15px/1.5 Inter, "Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif; background: #f4f4f6; color: #18181b; }
              body { min-height: 100%; margin: 0; display: grid; place-items: center; }
              main { width: min(460px, calc(100% - 48px)); padding: 32px; border-radius: 22px; background: #ffffff; box-shadow: 0 10px 30px rgba(0, 0, 0, 0.08), 0 0 0 0.5px rgba(0, 0, 0, 0.08); }
              h1 { margin: 0 0 8px; font-size: 20px; line-height: 1.25; }
              p { margin: 0 0 14px; color: rgba(24, 24, 27, 0.7); }
              code { display: block; margin: 0 0 20px; padding: 9px 12px; border-radius: 12px; background: rgba(0, 0, 0, 0.05); font: 12.5px/1.4 ui-monospace, Consolas, monospace; overflow-wrap: anywhere; }
              .retry { display: inline-block; padding: 8px 18px; border-radius: 999px; background: #0a84ff; color: #ffffff; font-weight: 500; text-decoration: none; }
              .retry:focus-visible { outline: 2px solid #0a84ff; outline-offset: 2px; }
              @media (prefers-color-scheme: dark) {
                html { background: #1c1c1e; color: #f4f4f6; }
                main { background: #2a2a2d; box-shadow: 0 12px 32px rgba(0, 0, 0, 0.4), 0 0 0 0.5px rgba(255, 255, 255, 0.1); }
                p { color: rgba(244, 244, 246, 0.7); }
                code { background: rgba(255, 255, 255, 0.08); }
              }
            </style>
            </head>
            <body>
            <main>
              <h1>{{WebUtility.HtmlEncode(title)}}</h1>
              <p>{{WebUtility.HtmlEncode(text)}}</p>
              <code>{{shown}}</code>
              {{retry}}
            </main>
            </body>
            </html>
            """;
    }
}
