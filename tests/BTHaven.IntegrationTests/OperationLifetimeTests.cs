using BTHaven_App;

namespace BTHaven.IntegrationTests;

public sealed class OperationLifetimeTests
{
    [Fact]
    public async Task Shutdown_rejects_new_work_and_waits_for_every_operation_before_cleanup()
    {
        var lifetime = new OperationLifetime();
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleaned = false;
        var one = lifetime.RunAsync(() => first.Task);
        var two = lifetime.RunAsync(() => second.Task);
        var shutdown = lifetime.ShutdownAsync(() => { }, () =>
        {
            cleaned = true;
            return ValueTask.CompletedTask;
        });

        Assert.Same(shutdown, lifetime.ShutdownAsync(() => throw new Exception("Second stop")));
        await lifetime.RunAsync(() => throw new Exception("Work started after shutdown"));
        first.SetResult();
        await one;
        Assert.False(shutdown.IsCompleted);
        Assert.False(cleaned);
        second.SetResult();
        await two;
        await shutdown.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(cleaned);
    }

    [Fact]
    public async Task Cleanup_continues_after_stop_operation_and_disposal_failures()
    {
        var lifetime = new OperationLifetime();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = lifetime.RunAsync(async () =>
        {
            await release.Task;
            throw new InvalidOperationException("Operation");
        });
        var cleaned = false;
        var shutdown = lifetime.ShutdownAsync(
            () => throw new InvalidOperationException("Stop"),
            () => throw new InvalidOperationException("Dispose"),
            () => { cleaned = true; return ValueTask.CompletedTask; });

        Assert.False(cleaned);
        release.SetResult();
        await Assert.ThrowsAsync<InvalidOperationException>(() => operation);
        var errors = await Assert.ThrowsAsync<AggregateException>(() => shutdown);
        Assert.True(cleaned);
        Assert.Equal(new[] { "Stop", "Operation", "Dispose" }, errors.InnerExceptions.Select(error => error.Message));
    }

    [Fact]
    public async Task Shutdown_started_inside_synchronous_operation_still_waits_for_that_operation()
    {
        var lifetime = new OperationLifetime();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? shutdown = null;
        var operation = lifetime.RunAsync(() =>
        {
            shutdown = lifetime.ShutdownAsync(() => { });
            return release.Task;
        });

        Assert.NotNull(shutdown);
        Assert.False(shutdown.IsCompleted);
        release.SetResult();
        await operation;
        await shutdown.WaitAsync(TimeSpan.FromSeconds(2));
    }
}
