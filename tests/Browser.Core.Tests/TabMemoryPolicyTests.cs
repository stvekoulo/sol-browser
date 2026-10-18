using Browser.Core;

namespace Browser.Core.Tests;

public sealed class TabMemoryPolicyTests
{
    [Theory]
    [InlineData(0, 300, 1800)]
    [InlineData(4096, 30, 600)]
    [InlineData(4097, 60, 900)]
    [InlineData(8192, 60, 900)]
    [InlineData(16384, 300, 1800)]
    public void UsesEarlierSleepOnSmallMachines(long memoryMb, int suspendSeconds, int discardSeconds)
    {
        var policy = TabMemoryPolicy.ForMemory(memoryMb);
        Assert.Equal(TimeSpan.FromSeconds(suspendSeconds), policy.SuspendAfter);
        Assert.Equal(TimeSpan.FromSeconds(discardSeconds), policy.DiscardAfter);
    }
}
