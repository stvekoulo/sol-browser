namespace Browser.Core;

public static class TabMemoryPolicy
{
    public static (TimeSpan SuspendAfter, TimeSpan DiscardAfter) ForMemory(long totalMb) => totalMb switch
    {
        > 0 and <= 4096 => (TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(10)),
        > 4096 and <= 8192 => (TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(15)),
        _ => (TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30)),
    };
}
