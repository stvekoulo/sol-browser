namespace Browser.Core;

/// <summary>Vue scindée : une paire d'onglets affichés côte à côte quand l'un des deux est actif.</summary>
public sealed class SplitLayout
{
    public string? PrimaryId { get; private set; }
    public string? SecondaryId { get; private set; }
    public double Ratio { get; private set; } = 0.5;
    public bool IsSplit => PrimaryId is not null;

    public bool Open(string primaryId, string secondaryId)
    {
        if (primaryId == secondaryId) return false;
        (PrimaryId, SecondaryId) = (primaryId, secondaryId);
        return true;
    }

    public void Close() => (PrimaryId, SecondaryId) = (null, null);
    public void Resize(double ratio) { if (double.IsFinite(ratio)) Ratio = Math.Clamp(ratio, 0.2, 0.8); }
    public static double FitRatio(double ratio, double width)
    {
        var minimum = width > 0 ? Math.Clamp(220 / width, 0.2, 0.5) : 0.5;
        return Math.Clamp(double.IsFinite(ratio) ? ratio : 0.5, minimum, 1 - minimum);
    }
    public bool Contains(string? id) => id is not null && (id == PrimaryId || id == SecondaryId);
    public void OnTabClosed(string id) { if (Contains(id)) Close(); }
}
