using BlazeHarness.Tests.Shared;
using Microsoft.Extensions.Time.Testing;
using System.Diagnostics;

#pragma warning disable VSTHRD003 // The helper waits for the caller's task on purpose.

namespace BlazeHarness.Core.Tests.Integration;

/// <summary>Helpers for driving code under test that waits on a <see cref="FakeTimeProvider"/>.</summary>
internal static class FakeTimeProviderExtensions
{
    /// <param name="time">The clock to advance.</param>
    extension(FakeTimeProvider time)
    {
        /// <summary>Advance the clock in steps until a task completes.</summary>
        /// <remarks>
        /// Use it when the code under test creates its timer on another thread, so the test can't know when advancing the clock starts to matter.
        /// The real-time bound only applies when the test is already failing.
        /// </remarks>
        /// <param name="task">The task that finishes once enough time has passed.</param>
        /// <param name="step">How far to advance the clock each time.</param>
        /// <returns>A task that completes when <paramref name="task"/> does.</returns>
        /// <exception cref="TimeoutException">The task didn't complete within <see cref="TestTimeouts.Default"/> of real time.</exception>
        public async Task AdvanceUntilAsync(Task task, TimeSpan step)
        {
            ArgumentNullException.ThrowIfNull(time);
            ArgumentNullException.ThrowIfNull(task);

            var ct = TestContext.Current.CancellationToken;
            var elapsed = Stopwatch.StartNew();
            while (!task.IsCompleted)
            {
                if (elapsed.Elapsed > TestTimeouts.Default)
                {
                    throw new TimeoutException("The task did not complete while the clock was advanced.");
                }

                time.Advance(step);
                await Task.WhenAny(task, Task.Delay(TimeSpan.FromMilliseconds(5), ct));
            }

            await task;
        }

        /// <summary>Advance the clock in steps until a task completes, then return its result.</summary>
        /// <typeparam name="T">The result type.</typeparam>
        /// <param name="task">The task that finishes once enough time has passed.</param>
        /// <param name="step">How far to advance the clock each time.</param>
        /// <returns>The result of <paramref name="task"/>.</returns>
        /// <exception cref="TimeoutException">The task didn't complete within <see cref="TestTimeouts.Default"/> of real time.</exception>
        public async Task<T> AdvanceUntilAsync<T>(Task<T> task, TimeSpan step)
        {
            await AdvanceUntilAsync(time, (Task)task, step);
            return await task;
        }
    }
}
