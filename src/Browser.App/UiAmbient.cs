using System.Text.Json;
using Browser.Core;
using Microsoft.UI.Xaml;

namespace Browser.App;

/// <summary>
/// Couleur vivante : l'interface suit ce que la page active montre à l'instant, pas seulement la couleur que le site
/// annonce. Une fois par seconde, la page est interrogée : s'il y a une vidéo en lecture à l'écran, elle en donne une
/// miniature de 16 × 9 points ; sinon, la couleur des fonds qui se trouvent sous quelques points de la fenêtre (elle
/// change donc avec le défilement). La teinte dominante devient la couleur d'accent ; sans couleur marquée, l'accent du
/// site reste. Le coût est celui d'une lecture de quelques pixels par seconde : aucune capture d'écran de la page.
/// Limites : une vidéo protégée (films sous DRM) ou placée dans un cadre d'un autre site ne se laisse pas lire.
/// </summary>
public sealed partial class UiBridge
{
    private static (byte R, byte G, byte B)? s_ambient;
    private DispatcherTimer? _ambientTimer;
    private bool _ambientBusy;
    private DateTime _ambientChanged;

    private static string? AmbientAccent => Settings?.LiveColor != false && s_ambient is var (r, g, b) ? $"rgb({r}, {g}, {b})" : null;

    // Renvoie des pixels « rouge, vert, bleu, alpha » à la suite, ou null s'il n'y a rien à lire.
    private const string AmbientScript = """
        (() => {
          if (document.hidden) return null;
          const seen = (v) => { const r = v.getBoundingClientRect(); return r.width > 200 && r.height > 110 && r.bottom > 0 && r.top < innerHeight; };
          const video = [...document.querySelectorAll('video')].find(v => !v.paused && !v.ended && v.readyState >= 2 && seen(v));
          if (video) {
            try {
              const c = window.__solAmbient || (window.__solAmbient = document.createElement('canvas'));
              c.width = 16; c.height = 9;
              const g = c.getContext('2d', { willReadFrequently: true });
              g.drawImage(video, 0, 0, 16, 9);
              return Array.from(g.getImageData(0, 0, 16, 9).data);
            } catch { /* vidéo illisible : on prend les couleurs de la page */ }
          }
          const out = [];
          const colors = document.createElement('canvas'); colors.width = colors.height = 1;
          const color = colors.getContext('2d', { willReadFrequently: true });
          if (!color) return null;
          for (const x of [0.25, 0.5, 0.75]) for (const y of [0.12, 0.5, 0.88]) {
            let el = document.elementFromPoint(innerWidth * x, innerHeight * y);
            for (let depth = 0; el && depth < 12; el = el.parentElement, depth++) {
              const css = getComputedStyle(el).backgroundColor;
              if (!CSS.supports('color', css)) continue;
              color.clearRect(0, 0, 1, 1); color.fillStyle = css; color.fillRect(0, 0, 1, 1);
              const pixel = color.getImageData(0, 0, 1, 1).data;
              if (pixel[3] > 127) { out.push(pixel[0], pixel[1], pixel[2], 255); break; }
            }
          }
          return out.length ? out : null;
        })()
        """;

    /// <summary>Met en route le suivi de la page active. Sans effet tant que le réglage est coupé.</summary>
    private void StartAmbient()
    {
        _ambientTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _ambientTimer.Tick += async (_, _) =>
        {
            if (_ambientBusy) return;
            _ambientBusy = true;
            try
            {
                var id = _activeId;
                await SampleOnceAsync(); // suit aussi un changement clair/sombre sans navigation
                var color = await ReadAmbientAsync();
                if (_activeId == id) SetAmbient(color);
            }
            finally { _ambientBusy = false; }
        };
        _ambientTimer.Start();
    }

    private async Task<(byte R, byte G, byte B)?> ReadAmbientAsync()
    {
        if (Settings?.LiveColor == false || pages is null || _activeId is null || pages.CoreOf(_activeId) is not { } core) return null;
        try
        {
            using var doc = JsonDocument.Parse(await core.ExecuteScriptAsync(AmbientScript));
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            var rgba = new byte[doc.RootElement.GetArrayLength()];
            var i = 0;
            foreach (var value in doc.RootElement.EnumerateArray()) rgba[i++] = (byte)Math.Clamp(value.GetDouble(), 0, 255);
            return AmbientColor.From(rgba);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException) { return null; }
    }

    private void SetAmbient((byte R, byte G, byte B)? color)
    {
        // Un écart infime (bruit d'une vidéo) ne vaut pas un changement de couleur de toute l'interface.
        var moved = color is null != s_ambient is null || (color is { } now && s_ambient is { } before && AmbientColor.Distance(now, before) > 30);
        if (!moved) return;
        // Changer la couleur fait redessiner toute l'interface : c'est le seul coût réel de ce suivi (la lecture, elle,
        // ne se mesure pas). Pas plus d'un changement toutes les 2,5 secondes, même devant une vidéo très changeante.
        if (color is not null && s_ambient is not null && DateTime.UtcNow - _ambientChanged < TimeSpan.FromSeconds(2.5)) return;
        _ambientChanged = DateTime.UtcNow;
        s_ambient = color;
        BroadcastTheme();
    }
}
