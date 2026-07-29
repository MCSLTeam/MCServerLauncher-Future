using System;
using System.Threading;
using System.Threading.Tasks;
using MCServerLauncher.WPF.InstanceConsole.Modules;

namespace MCServerLauncher.WPF.Tests;

public sealed class InstanceDataManagerLatencyTests
{
    [Fact]
    public async Task GetDaemonLatencyAsyncHonorsCancellationBeforeCheckingDaemonState()
    {
        await InstanceDataManager.Instance.DisposeAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            InstanceDataManager.Instance.GetDaemonLatencyAsync(cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }
}