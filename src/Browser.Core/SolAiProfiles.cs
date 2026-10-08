namespace Browser.Core;

public sealed record SolAiProfile(string Alias, string Name, int Rank, int RamMb, int WorkingMb, int Threads);

public static class SolAiProfiles
{
    // Identités internes : les noms publics Sol restent stables quand le moteur/catalogue évolue.
    public static readonly SolAiProfile[] All =
    [
        new("smollm3-3b", "Sol AI", 0, 8192, 2816, 4),
    ];
    // Budgets prudents pour l'inférence texte courte sur CPU, avec une réserve pour Windows et Sol.
    // Ce sont des règles produit, pas des mesures de vitesse ou des minimums garantis du fournisseur.
    public static bool Fits(SolAiProfile profile, long totalMb, long availableMb, int threads, long freeDiskMb, int sizeMb, bool cached = false, long? commitMb = null) =>
        totalMb >= profile.RamMb - 256 && availableMb >= (commitMb.HasValue ? 512 : profile.WorkingMb + 512)
        && (!commitMb.HasValue || commitMb.Value >= profile.WorkingMb + 512)
        && threads >= profile.Threads && sizeMb > 0 && freeDiskMb >= (cached ? 128 : sizeMb * 2L + 512);
}
