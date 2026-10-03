namespace Browser.Core;

/// <summary>
/// Domaines de publicité et de pistage. Une requête vers l'un d'eux (ou l'un de ses sous-domaines) est bloquée quand
/// elle vient d'un autre site : aller soi-même sur un de ces sites reste possible.
/// </summary>
public sealed class BlockList
{
    private readonly HashSet<string> _domains;

    public BlockList(IEnumerable<string> domains) => _domains = new HashSet<string>(domains, StringComparer.OrdinalIgnoreCase);

    public int Count => _domains.Count;

    /// <summary>Le domaine de la liste qui couvre cet hôte (« stats.g.doubleclick.net » → « doubleclick.net »), ou null.</summary>
    public string? Match(string host)
    {
        for (var rest = host.TrimEnd('.'); rest.Contains('.'); rest = rest[(rest.IndexOf('.') + 1)..])
            if (_domains.Contains(rest)) return rest.ToLowerInvariant();
        return null;
    }

    /// <summary>Vrai si la requête va vers un domaine de la liste depuis une page d'un autre site.</summary>
    public bool Blocks(string requestUrl, string? pageUrl)
    {
        if (!Uri.TryCreate(requestUrl, UriKind.Absolute, out var request) || request.Scheme is not ("http" or "https" or "ws" or "wss")) return false;
        if (Match(request.Host) is not { } blocked) return false;
        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var page)) return true;
        return !(page.Host.Equals(blocked, StringComparison.OrdinalIgnoreCase) || page.Host.EndsWith("." + blocked, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Lit une liste publique : un domaine par ligne, ou au format « hosts » (« 0.0.0.0 domaine »), ou au format des
    /// filtres (« ||domaine^ »). Tout ce qui n'est pas un nom de domaine simple est ignoré.
    /// </summary>
    public static IEnumerable<string> Parse(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Split('#')[0].Trim();
            if (line.Length == 0 || line[0] == '!') continue;
            var fields = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            var domain = fields[^1];
            if (fields.Length == 2 && fields[0] is not ("0.0.0.0" or "127.0.0.1")) continue;
            if (fields.Length > 2) continue;
            if (domain.StartsWith("||", StringComparison.Ordinal) && domain.EndsWith('^')) domain = domain[2..^1];
            if (IsDomain(domain)) yield return domain.ToLowerInvariant();
        }
    }

    private static bool IsDomain(string text) =>
        text.Length is >= 4 and <= 253 && text.Contains('.') && text[0] != '.' && text[^1] != '.' && !text.Contains("..")
        && text.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-') && text is not ("localhost" or "0.0.0.0");

    /// <summary>Liste de départ, livrée avec le navigateur : les régies et traqueurs les plus répandus.</summary>
    public static readonly IReadOnlyList<string> BuiltIn =
    [
        // Régies publicitaires
        "doubleclick.net", "googlesyndication.com", "googleadservices.com", "googletagservices.com", "adservice.google.com",
        "2mdn.net", "adnxs.com", "adsrvr.org", "amazon-adsystem.com", "criteo.com", "criteo.net", "taboola.com", "outbrain.com",
        "pubmatic.com", "rubiconproject.com", "openx.net", "casalemedia.com", "indexww.com", "smartadserver.com", "adform.net",
        "media.net", "moatads.com", "teads.tv", "sharethrough.com", "33across.com", "bidswitch.net", "yieldmo.com", "triplelift.com",
        "3lift.com", "gumgum.com", "adcolony.com", "applovin.com", "unityads.unity3d.com", "inmobi.com", "mopub.com", "smaato.net",
        "advertising.com", "adtechus.com", "contextweb.com", "lijit.com", "sovrn.com", "spotxchange.com", "spotx.tv", "tremorhub.com",
        "revcontent.com", "mgid.com", "zedo.com", "propellerads.com", "popads.net", "popcash.net", "adsterra.com", "exoclick.com",
        "juicyads.com", "trafficjunky.net", "adroll.com", "bidr.io", "a-ads.com", "buysellads.com", "carbonads.com", "carbonads.net",
        "yandexadexchange.net", "adriver.ru", "ad.gt", "undertone.com", "kargo.com", "sonobi.com", "emxdgt.com", "onetag-sys.com",
        "seedtag.com", "adkernel.com", "stickyadstv.com", "freewheel.tv", "fwmrm.net", "innovid.com", "flashtalking.com",
        "serving-sys.com", "sizmek.com", "ads-twitter.com", "ads.linkedin.com", "ads.pinterest.com", "ads.tiktok.com",
        "ads.yahoo.com", "adsymptotic.com", "bat.bing.com", "ads.reddit.com",
        // Mesure d'audience et pistage
        "google-analytics.com", "googletagmanager.com", "analytics.google.com", "stats.g.doubleclick.net", "scorecardresearch.com",
        "quantserve.com", "quantcount.com", "hotjar.com", "hotjar.io", "mouseflow.com", "fullstory.com", "crazyegg.com",
        "luckyorange.com", "clarity.ms", "mixpanel.com", "amplitude.com", "segment.io", "heap.io", "heapanalytics.com",
        "matomo.cloud", "chartbeat.com", "chartbeat.net", "nr-data.net", "optimizely.com", "branch.io", "app-measurement.com",
        "appsflyer.com", "adjust.com", "kochava.com", "singular.net", "connect.facebook.net", "pixel.facebook.com",
        "analytics.tiktok.com", "analytics.twitter.com", "snap.licdn.com", "px.ads.linkedin.com", "ct.pinterest.com", "tr.snapchat.com",
        "sc-static.net", "bluekai.com", "krxd.net", "demdex.net", "omtrdc.net", "everesttech.net", "2o7.net", "adsafeprotected.com",
        "doubleverify.com", "imrworldwide.com", "exelator.com", "eyeota.net", "rlcdn.com", "agkn.com", "crwdcntrl.net", "tapad.com",
        "liadm.com", "id5-sync.com", "mathtag.com", "turn.com", "addthis.com", "sharethis.com", "outbrainimg.com", "zemanta.com",
        "mc.yandex.ru", "counter.yadro.ru", "top.mail.ru", "statcounter.com", "histats.com", "parsely.com",
        "permutive.com", "permutive.app", "cxense.com",
    ];
}
