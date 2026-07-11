using System.Diagnostics;
using System.Reflection;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.DaemonClient;

namespace MCServerLauncher.ProtocolTests;

public class LegacyDaemonClientRestartMigrationCharacterizationTests
{
    [Fact]
    [Trait("Category", "LegacyDaemonClientRestartMigration")]
    public async Task LegacyDaemonClientRestartMigration_StopsThenWaitsApproximatelyOneSecondThenStarts()
    {
        var instanceId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        const int timeout = 4321;
        using var cancellationSource = new CancellationTokenSource();
        var stopwatch = Stopwatch.StartNew();
        var calls = new List<LegacyRestartCall>();

        var daemon = CreateProxy<IDaemon>((method, args) =>
        {
            if (method.Name == nameof(IDaemon.RequestAsync) && !method.IsGenericMethod && args is { Length: 4 })
            {
                calls.Add(new LegacyRestartCall(
                    (ActionType)args[0]!,
                    (IActionParameter?)args[1],
                    (int)args[2]!,
                    (CancellationToken)args[3]!,
                    stopwatch.Elapsed));
                return Task.CompletedTask;
            }

            return GetDefaultReturnValue(method.ReturnType);
        });

        await daemon.RestartInstanceAsync(instanceId, timeout, cancellationSource.Token);

        Assert.Collection(
            calls,
            stopCall =>
            {
                Assert.Equal(ActionType.StopInstance, stopCall.ActionType);
                Assert.Equal(instanceId, Assert.IsType<StopInstanceParameter>(stopCall.Parameter).Id);
                Assert.Equal(timeout, stopCall.Timeout);
                Assert.Equal(cancellationSource.Token, stopCall.CancellationToken);
            },
            startCall =>
            {
                Assert.Equal(ActionType.StartInstance, startCall.ActionType);
                Assert.Equal(instanceId, Assert.IsType<StartInstanceParameter>(startCall.Parameter).Id);
                Assert.Equal(timeout, startCall.Timeout);
                Assert.Equal(cancellationSource.Token, startCall.CancellationToken);
            });

        var compositionDelay = calls[1].Elapsed - calls[0].Elapsed;
        Assert.InRange(compositionDelay, TimeSpan.FromMilliseconds(900), TimeSpan.FromSeconds(5));
    }

    [Fact]
    [Trait("Category", "LegacyDaemonClientRestartMigration")]
    public async Task LegacyDaemonClientRestartMigration_StopFailure_PropagatesWithoutStarting()
    {
        var expectedFailure = new InvalidOperationException("legacy stop failed");
        var calls = new List<ActionType>();
        var daemon = CreateProxy<IDaemon>((method, args) =>
        {
            if (method.Name == nameof(IDaemon.RequestAsync) && !method.IsGenericMethod && args is { Length: 4 })
            {
                var actionType = (ActionType)args[0]!;
                calls.Add(actionType);
                return actionType == ActionType.StopInstance
                    ? Task.FromException(expectedFailure)
                    : Task.CompletedTask;
            }

            return GetDefaultReturnValue(method.ReturnType);
        });

        var actualFailure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => daemon.RestartInstanceAsync(Guid.NewGuid()));

        Assert.Same(expectedFailure, actualFailure);
        Assert.Equal([ActionType.StopInstance], calls);
    }

    [Fact]
    [Trait("Category", "LegacyDaemonClientRestartMigration")]
    public async Task LegacyDaemonClientRestartMigration_DelayCancellation_PropagatesWithoutStarting()
    {
        using var cancellationSource = new CancellationTokenSource();
        var calls = new List<ActionType>();
        var daemon = CreateProxy<IDaemon>((method, args) =>
        {
            if (method.Name == nameof(IDaemon.RequestAsync) && !method.IsGenericMethod && args is { Length: 4 })
            {
                var actionType = (ActionType)args[0]!;
                calls.Add(actionType);
                if (actionType == ActionType.StopInstance)
                    cancellationSource.CancelAfter(TimeSpan.FromMilliseconds(25));

                return Task.CompletedTask;
            }

            return GetDefaultReturnValue(method.ReturnType);
        });

        var cancellation = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => daemon.RestartInstanceAsync(Guid.NewGuid(), ct: cancellationSource.Token));

        Assert.Equal(cancellationSource.Token, cancellation.CancellationToken);
        Assert.Equal([ActionType.StopInstance], calls);
    }

    private static T CreateProxy<T>(Func<MethodInfo, object?[]?, object?> handler)
        where T : class
    {
        var proxy = DispatchProxy.Create<T, InterfaceDispatchProxy>();
        ((InterfaceDispatchProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    private static object? GetDefaultReturnValue(Type returnType)
    {
        if (returnType == typeof(void))
            return null;
        if (returnType == typeof(Task))
            return Task.CompletedTask;

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var resultType = returnType.GetGenericArguments()[0];
            var resultValue = resultType.IsValueType ? Activator.CreateInstance(resultType) : null;
            return typeof(Task).GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(resultType)
                .Invoke(null, [resultValue]);
        }

        return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
    }

    private readonly record struct LegacyRestartCall(
        ActionType ActionType,
        IActionParameter? Parameter,
        int Timeout,
        CancellationToken CancellationToken,
        TimeSpan Elapsed);

    private class InterfaceDispatchProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = (_, _) => null;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return Handler(targetMethod ?? throw new MissingMethodException("DispatchProxy target method was null."), args);
        }
    }
}
