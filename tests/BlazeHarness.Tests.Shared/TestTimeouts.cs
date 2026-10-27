namespace BlazeHarness.Tests.Shared;

/// <summary>Bounds for waiting in tests. They only matter when a test is already failing, so they are generous.</summary>
public static class TestTimeouts
{
    /// <summary>Gets how long a test may wait for something to happen before it fails.</summary>
    public static readonly TimeSpan Default = TimeSpan.FromSeconds(30);

    /// <summary>Wait for a task, failing with a <see cref="TimeoutException"/> after <see cref="Default"/>.</summary>
    /// <param name="task">The task to wait for.</param>
    /// <returns>A task that completes with <paramref name="task"/>.</returns>
    public static Task Bounded(this Task task) => task.WaitAsync(Default, TestContext.Current.CancellationToken);

    /// <summary>Wait for a task, failing with a <see cref="TimeoutException"/> after <see cref="Default"/>.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="task">The task to wait for.</param>
    /// <returns>A task that completes with <paramref name="task"/>.</returns>
    public static Task<T> Bounded<T>(this Task<T> task) => task.WaitAsync(Default, TestContext.Current.CancellationToken);
}
