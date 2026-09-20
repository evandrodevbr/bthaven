namespace BTHaven_App;

// Registers work before invoking it, including synchronous/reentrant operations.
internal sealed class OperationLifetime
{
    private readonly object sync = new();
    private readonly HashSet<Task> pending = [];
    private Task? shutdown;

    public Task RunAsync(Func<Task> operation)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (sync)
        {
            if (shutdown is not null)
            {
                return Task.CompletedTask;
            }
            pending.Add(completion.Task);
        }
        _ = CompleteAsync(operation, completion);
        return completion.Task;
    }

    private async Task CompleteAsync(Func<Task> operation, TaskCompletionSource completion)
    {
        try
        {
            await operation();
            completion.TrySetResult();
        }
        catch (OperationCanceledException exception)
        {
            completion.TrySetCanceled(exception.CancellationToken);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
        finally
        {
            lock (sync)
            {
                pending.Remove(completion.Task);
            }
        }
    }

    public Task ShutdownAsync(Action stop, params Func<ValueTask>[] cleanup)
    {
        TaskCompletionSource completion;
        Task[] running;
        lock (sync)
        {
            if (shutdown is not null)
            {
                return shutdown;
            }
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            shutdown = completion.Task;
            running = pending.ToArray();
        }
        _ = ShutdownCoreAsync(stop, cleanup, running, completion);
        return completion.Task;
    }

    private static async Task ShutdownCoreAsync(
        Action stop,
        Func<ValueTask>[] cleanup,
        Task[] running,
        TaskCompletionSource completion)
    {
        List<Exception> errors = [];
        try { stop(); }
        catch (Exception exception) { errors.Add(exception); }
        try { await Task.WhenAll(running); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { errors.Add(exception); }
        foreach (var dispose in cleanup)
        {
            try { await dispose(); }
            catch (Exception exception) { errors.Add(exception); }
        }
        if (errors.Count == 0)
        {
            completion.TrySetResult();
        }
        else
        {
            completion.TrySetException(new AggregateException(errors));
        }
    }
}
