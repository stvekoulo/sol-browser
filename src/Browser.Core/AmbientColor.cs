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

    /// <summary>Écart entre deux couleurs, de 0 (identiques) à 765.</summary>
    public static int Distance((byte R, byte G, byte B) a, (byte R, byte G, byte B) b) =>
        Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
}
