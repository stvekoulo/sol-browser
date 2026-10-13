namespace Browser.Core;

/// <summary>
/// Couleur d'ambiance de ce qui est affiché : la teinte dominante d'une petite image de la page (une vidéo en cours,
/// la partie de la page à l'écran). Les pixels vifs comptent le plus ; le noir, le blanc et les gris ne comptent pas.
/// </summary>
public static class AmbientColor
{
    /// <param name="rgba">Pixels sur quatre octets : rouge, vert, bleu, alpha.</param>
    /// <returns>La couleur dominante, ou null si l'image n'a pas de couleur marquée.</returns>
    public static (byte R, byte G, byte B)? From(ReadOnlySpan<byte> rgba)
    {
        double red = 0, green = 0, blue = 0, total = 0;
        var pixels = rgba.Length / 4;
        for (var i = 0; i + 3 < rgba.Length; i += 4)
        {
            int r = rgba[i], g = rgba[i + 1], b = rgba[i + 2];
            int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            if (max == 0) continue;
            var saturation = (max - min) / (double)max;
            // Poids : la vivacité, et la clarté (un pixel presque noir, même saturé, ne donne pas le ton).
            var weight = saturation * saturation * (max / 255.0);
            red += r * weight; green += g * weight; blue += b * weight; total += weight;
        }
        // Trop peu de couleur dans l'ensemble (page blanche, texte noir, image grise) : pas d'ambiance.
        if (pixels == 0 || total / pixels < 0.02) return null;
        return ((byte)Math.Round(red / total), (byte)Math.Round(green / total), (byte)Math.Round(blue / total));
    }

    /// <summary>
    /// Couleur des bords d'une page : celle que partagent, à peu de chose près, la plupart des pixels relevés le long de
    /// ses bords. Null si aucune couleur ne l'emporte (bords en image ou en dégradé marqué) : rien de fiable à prolonger.
    /// </summary>
    public static (byte R, byte G, byte B)? Edge(IReadOnlyList<(byte R, byte G, byte B)> samples)
    {
        const int near = 18; // un dégradé léger ou le bruit d'une image compressée restent une seule couleur
        var most = 0;
        (byte R, byte G, byte B) center = default;
        foreach (var candidate in samples)
        {
            var count = samples.Count(other => Distance(candidate, other) <= near);
            if (count > most) (most, center) = (count, candidate);
        }
        if (most == 0 || most * 2 < samples.Count) return null;
        var alike = samples.Where(sample => Distance(center, sample) <= near).ToList();
        return ((byte)Math.Round(alike.Average(c => c.R)), (byte)Math.Round(alike.Average(c => c.G)), (byte)Math.Round(alike.Average(c => c.B)));
    }

    /// <summary>Écart entre deux couleurs, de 0 (identiques) à 765.</summary>
    public static int Distance((byte R, byte G, byte B) a, (byte R, byte G, byte B) b) =>
        Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
}
