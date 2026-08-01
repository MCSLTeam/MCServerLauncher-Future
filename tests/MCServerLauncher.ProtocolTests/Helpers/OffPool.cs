namespace MCServerLauncher.ProtocolTests.Helpers;

/// <summary>
/// Starts work on a dedicated thread rather than a thread-pool thread.
/// </summary>
/// <remarks>
/// Tests that deliberately block — to poise a race, or to bridge a synchronous production
/// interface — must not hold a pool thread while doing so. The code under test needs the same
/// pool to make progress, and on a two-core runner a handful of parked workers is enough to
/// stall every other collection until the pool injects replacements.
/// </remarks>
internal static class OffPool
{
    internal static Task Run(Action work) => Task.Factory.StartNew(
        work,
        CancellationToken.None,
        TaskCreationOptions.LongRunning,
        TaskScheduler.Default);

    internal static Task<T> Run<T>(Func<T> work) => Task.Factory.StartNew(
        work,
        CancellationToken.None,
        TaskCreationOptions.LongRunning,
        TaskScheduler.Default);

    internal static Task RunAsync(Func<Task> work) => Task.Factory.StartNew(
        work,
        CancellationToken.None,
        TaskCreationOptions.LongRunning,
        TaskScheduler.Default).Unwrap();

    internal static Task<T> RunAsync<T>(Func<Task<T>> work) => Task.Factory.StartNew(
        work,
        CancellationToken.None,
        TaskCreationOptions.LongRunning,
        TaskScheduler.Default).Unwrap();
}
