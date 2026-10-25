namespace Browser.App;

/// <summary>Share one initialization, but let the next user attempt retry after a failure.</summary>
internal sealed class StartupTask<T>(Func<Task<T>> create)
{
    private readonly object _gate = new();
    private Task<T>? _task;
    public Task<T> GetAsync()
    {
        lock (_gate)
        {
            if (_task is null || _task.IsFaulted || _task.IsCanceled) _task = create();
            return _task;
        }
    }
}
