namespace Waddamburo.Platform.Prospero;

/// <summary>A fixed worker/stack budget for the console, initialized before asset work.</summary>
public static class ProsperoThreadPool
{
    public const int Workers = 8;

    /// <summary>Run once on the bootstrap thread, before background work starts.</summary>
    public static void Initialize()
    {
        if (Thread.CurrentThread.IsThreadPoolThread)
            throw new InvalidOperationException("Initialize the PS5 worker pool on the bootstrap thread.");
        ThreadPool.GetMinThreads(out _, out var minimumPorts);
        ThreadPool.GetMaxThreads(out _, out var maximumPorts);
        if (!ThreadPool.SetMinThreads(Workers, minimumPorts) || !ThreadPool.SetMaxThreads(Workers, maximumPorts))
            throw new InvalidOperationException("Could not apply the PS5 worker thread budget.");

        // Reserve all worker stacks while startup still has headroom, instead of
        // asking the OS for another stack in the middle of a busy song/load.
        using var ready = new CountdownEvent(Workers);
        using var release = new ManualResetEventSlim();
        var jobs = new Task?[Workers];
        try
        {
            for (var i = 0; i < jobs.Length; i++)
                jobs[i] = Task.Run(() => { ready.Signal(); release.Wait(); });
            if (!ready.Wait(TimeSpan.FromSeconds(10)))
                throw new InvalidOperationException("The PS5 worker pool did not start within its time limit.");
        }
        finally
        {
            release.Set();
            Task.WaitAll(jobs.OfType<Task>().ToArray());
        }
    }
}
