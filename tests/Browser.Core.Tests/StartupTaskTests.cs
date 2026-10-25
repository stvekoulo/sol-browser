using Browser.App;

public sealed class StartupTaskTests
{
    [Fact]
    public async Task Concurrent_tabs_share_initialization_and_next_user_attempt_retries_a_failure()
    {
        var attempts = 0;
        var first = new TaskCompletionSource<int>();
        var engine = new StartupTask<int>(() => ++attempts == 1 ? first.Task : Task.FromResult(42));
        var a = engine.GetAsync();
        var b = engine.GetAsync();
        Assert.Same(a, b);
        first.SetException(new InvalidOperationException("temporary engine failure"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => a);
        await Assert.ThrowsAsync<InvalidOperationException>(() => b);
        Assert.Equal(42, await engine.GetAsync());
        Assert.Equal(42, await engine.GetAsync());
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task Canceled_initialization_can_be_retried_without_a_restart()
    {
        var attempts = 0;
        var engine = new StartupTask<int>(() => ++attempts == 1
            ? Task.FromCanceled<int>(new CancellationToken(true)) : Task.FromResult(7));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.GetAsync());
        Assert.Equal(7, await engine.GetAsync());
    }

    [Fact]
    public async Task Optional_preparation_failure_does_not_stop_the_next_step()
    {
        var errors = new List<string>();
        var nextRan = false;
        await StartupPreparation.OptionalAsync("extensions", () => throw new IOException("locked file"),
            (stage, error) => errors.Add(stage));
        await StartupPreparation.OptionalAsync("next", () => { nextRan = true; return Task.CompletedTask; },
            (stage, error) => errors.Add(stage));
        Assert.Equal(["extensions"], errors);
        Assert.True(nextRan);
    }

    [Fact]
    public async Task Shutdown_and_unexpected_failures_are_not_silently_ignored()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            StartupPreparation.OptionalAsync("extensions", () => throw new OperationCanceledException(), (_, _) => { }));
        await Assert.ThrowsAsync<NullReferenceException>(() =>
            StartupPreparation.OptionalAsync("extensions", () => throw new NullReferenceException(), (_, _) => { }));
    }
}
