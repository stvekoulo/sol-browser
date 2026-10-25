namespace Browser.Core;

public static class SidePanelLayout
{
    public const double SidebarMinimum = 220, SidebarMaximum = 360, SidebarDefault = 296;
    public const double ChatMinimum = 280, ChatMaximum = 480, ChatDefault = 380;

    public static double Sidebar(double requested, double viewportWidth, double scale)
    {
        var maximum = SidebarMaximum * scale;
        if (double.IsFinite(viewportWidth) && viewportWidth > 0)
            maximum = Math.Min(maximum, Math.Max(SidebarMinimum * scale, viewportWidth * 0.35));
        return Math.Clamp(double.IsFinite(requested) ? Math.Round(requested) : SidebarDefault * scale,
            SidebarMinimum * scale, maximum);
    }

    public static double Chat(double requested, double available, double scale, bool overlay)
    {
        available = double.IsFinite(available) ? Math.Max(0, available) : 0;
        var maximum = Math.Min(ChatMaximum * scale, available);
        // En mode ancré, réserver une largeur lisible à la page entre les deux volets.
        if (!overlay) maximum = Math.Min(maximum, Math.Max(0, available - 320 * scale));
        return Math.Clamp(double.IsFinite(requested) ? requested : ChatDefault * scale,
            Math.Min(ChatMinimum * scale, maximum), maximum);
    }
}
