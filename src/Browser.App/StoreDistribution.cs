namespace Browser.App;

/// <summary>Le paquet Store confie exclusivement ses mises à jour à Microsoft.</summary>
internal static class StoreDistribution
{
    public static bool Enabled =>
#if SOL_STORE
        true;
#else
        false;
#endif
}
