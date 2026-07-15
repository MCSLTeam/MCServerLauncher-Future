using System.Reflection;
using System.Text.Json;
using MCServerLauncher.Common.Contracts.EventRules;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.ApplicationCore.Events;
using MCServerLauncher.Daemon.Remote.Event;
using Microsoft.Extensions.Logging;
using RustyOptions;

namespace MCServerLauncher.ProtocolTests;

public sealed class DomainEventPortAndTriggerTests
{
    [Fact]
    public async Task PublishAsync_AwaitsSubscribersSequentiallyInRegistrationOrder()
    {
        using var host = DomainEventPortTestHost.Create();
        var port = host.Port;
        var owner = port.CreateOwner("sequential-owner");
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var order = new List<string>();
        port.Subscribe<InstanceLogDomainEvent>(owner, async (_, _) =>
        {
            order.Add("first-enter");
            firstEntered.SetResult();
            await releaseFirst.Task;
            order.Add("first-exit");
        });
        port.Subscribe<InstanceLogDomainEvent>(owner, (_, _) =>
        {
            order.Add("second");
            return ValueTask.CompletedTask;
        });

        var publish = port.PublishAsync(
            new InstanceLogDomainEvent(Guid.NewGuid(), "ready")).AsTask();
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(["first-enter"], order);

        releaseFirst.SetResult();
        await publish.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(["first-enter", "first-exit", "second"], order);
    }

    [Fact]
    public async Task PublishAsync_IsolatesSubscriberExceptionsAndContinuesDispatch()
    {
        var logger = new RecordingLogger<DomainEventPort>();
        using var host = DomainEventPortTestHost.Create(logger);
        var port = host.Port;
        var owner = port.CreateOwner("exception-owner");
        var delivered = 0;
        port.Subscribe<InstanceLogDomainEvent>(owner, (_, _) =>
            ValueTask.FromException(new InvalidOperationException("subscriber failed")));
        port.Subscribe<InstanceLogDomainEvent>(owner, (_, _) =>
        {
            delivered++;
            return ValueTask.CompletedTask;
        });

        var exception = await Record.ExceptionAsync(async () =>
            await port.PublishAsync(new InstanceLogDomainEvent(Guid.NewGuid(), "ready")));

        Assert.Null(exception);
        Assert.Equal(1, delivered);
        var entry = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error);
        Assert.IsType<InvalidOperationException>(entry.Exception);
        Assert.Equal("exception-owner", entry.Properties["Owner"]);
        Assert.Contains(nameof(InstanceLogDomainEvent), entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishAsync_MatchingCancellationPropagatesAndStopsDispatch()
    {
        using var host = DomainEventPortTestHost.Create();
        var port = host.Port;
        var owner = port.CreateOwner("matching-cancellation-owner");
        using var cancellation = new CancellationTokenSource();
        var laterDeliveries = 0;
        port.Subscribe<InstanceLogDomainEvent>(owner, (_, token) =>
        {
            cancellation.Cancel();
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            return ValueTask.FromException(new OperationCanceledException(linkedCancellation.Token));
        });
        port.Subscribe<InstanceLogDomainEvent>(owner, (_, _) =>
        {
            laterDeliveries++;
            return ValueTask.CompletedTask;
        });

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await port.PublishAsync(
                new InstanceLogDomainEvent(Guid.NewGuid(), "cancel"),
                cancellation.Token));

        Assert.True(exception.CancellationToken.IsCancellationRequested);
        Assert.Equal(0, laterDeliveries);
    }

    [Fact]
    public async Task PublishAsync_ForeignCancellationLogsAndContinuesDispatch()
    {
        var logger = new RecordingLogger<DomainEventPort>();
        using var host = DomainEventPortTestHost.Create(logger);
        var port = host.Port;
        var owner = port.CreateOwner("foreign-cancellation-owner");
        using var foreignCancellation = new CancellationTokenSource();
        foreignCancellation.Cancel();
        var laterDeliveries = 0;
        port.Subscribe<InstanceLogDomainEvent>(owner, (_, _) =>
            ValueTask.FromException(new OperationCanceledException(foreignCancellation.Token)));
        port.Subscribe<InstanceLogDomainEvent>(owner, (_, _) =>
        {
            laterDeliveries++;
            return ValueTask.CompletedTask;
        });

        await port.PublishAsync(new InstanceLogDomainEvent(Guid.NewGuid(), "foreign"));

        Assert.Equal(1, laterDeliveries);
        var entry = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error);
        Assert.IsType<OperationCanceledException>(entry.Exception);
    }

    [Fact]
    public async Task PublishAsync_ConcurrentPublishersMayOverlapSameSubscriber()
    {
        using var host = DomainEventPortTestHost.Create();
        var port = host.Port;
        var owner = port.CreateOwner("concurrent-owner");
        var bothEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        port.Subscribe<InstanceLogDomainEvent>(owner, async (_, _) =>
        {
            if (Interlocked.Increment(ref active) == 2)
                bothEntered.TrySetResult();
            await release.Task;
            Interlocked.Decrement(ref active);
        });

        var first = port.PublishAsync(new InstanceLogDomainEvent(Guid.NewGuid(), "first")).AsTask();
        var second = port.PublishAsync(new InstanceLogDomainEvent(Guid.NewGuid(), "second")).AsTask();
        await bothEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(2, Volatile.Read(ref active));

        release.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task OwnerLedger_DisposeIsIdempotentAndLeavesNoResidualSubscription()
    {
        using var host = DomainEventPortTestHost.Create();
        var port = host.Port;
        var owner = port.CreateOwner("ledger-owner");
        var delivered = 0;
        port.Subscribe<InstanceLogDomainEvent>(owner, (_, _) =>
        {
            delivered++;
            return ValueTask.CompletedTask;
        });
        Assert.Equal(1, port.ActiveSubscriptionCount);

        port.DisposeOwner(owner);
        port.DisposeOwner(owner);
        Assert.Equal(0, port.ActiveSubscriptionCount);
        await port.PublishAsync(new InstanceLogDomainEvent(Guid.NewGuid(), "ignored"));

        Assert.Equal(0, delivered);
        Assert.Throws<ObjectDisposedException>(() =>
            port.Subscribe<InstanceLogDomainEvent>(owner, (_, _) => ValueTask.CompletedTask));
    }

    [Fact]
    public async Task SlowSubscriber_UsesInjectableClockAndLogsWarning()
    {
        var logger = new RecordingLogger<DomainEventPort>();
        var clock = new StepClock(0, 20);
        using var host = DomainEventPortTestHost.Create(
            logger,
            new DomainEventDispatchPolicy(TimeSpan.FromMilliseconds(10), clock));
        var port = host.Port;
        var owner = port.CreateOwner("slow-owner");
        port.Subscribe<InstanceLogDomainEvent>(owner, (_, _) => ValueTask.CompletedTask);

        await port.PublishAsync(new InstanceLogDomainEvent(Guid.NewGuid(), "slow"));

        var entry = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Equal("slow-owner", entry.Properties["Owner"]);
        Assert.Equal(20d, entry.Properties["ElapsedMilliseconds"]);
    }

    [Fact]
    public async Task SubscriptionAndTriggerLifecycle_AreIdempotentAndStopNewDelivery()
    {
        using var host = DomainEventPortTestHost.Create();
        var port = host.Port;
        var directOwner = port.CreateOwner("direct-owner");
        var directDeliveries = 0;
        port.Subscribe<InstanceLogDomainEvent>(directOwner, (_, _) =>
        {
            directDeliveries++;
            return ValueTask.CompletedTask;
        });
        await port.PublishAsync(new InstanceLogDomainEvent(Guid.NewGuid(), "first"));
        port.DisposeOwner(directOwner);
        port.DisposeOwner(directOwner);
        await port.PublishAsync(new InstanceLogDomainEvent(Guid.NewGuid(), "second"));
        Assert.Equal(1, directDeliveries);

        var ruleReads = 0;
        var application = CreateApplication(
            getRules: instanceId =>
            {
                ruleReads++;
                return Result.Ok<EventRuleSet, DaemonError>(CreateRuleSet(instanceId, []));
            });
        var service = new EventTriggerService(application, port, new RecordingLogger<EventTriggerService>());
        var targetId = Guid.NewGuid();

        await port.PublishAsync(new InstanceLogDomainEvent(targetId, "running"));
        Assert.Equal(1, ruleReads);

        service.Stop();
        service.Stop();
        await port.PublishAsync(new InstanceLogDomainEvent(targetId, "stopped"));
        Assert.Equal(1, ruleReads);

        service.Start();
        service.Start();
        await port.PublishAsync(new InstanceLogDomainEvent(targetId, "restarted"));
        Assert.Equal(2, ruleReads);

        service.Dispose();
        service.Dispose();
        await port.PublishAsync(new InstanceLogDomainEvent(targetId, "disposed"));
        Assert.Equal(2, ruleReads);
        Assert.Throws<ObjectDisposedException>(service.Start);
        await service.DisposeAsync();
    }

    [Fact]
    public async Task EventTriggerService_CanceledDrainCanBeRetriedByDispose()
    {
        using var host = DomainEventPortTestHost.Create();
        var port = host.Port;
        var actionEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAction = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rule = new EventRule
        {
            IsEnabled = true,
            Triggers = [new ConsoleOutputTrigger { Pattern = "ready" }],
            Actions = [new SendCommandAction { Command = "say shutdown" }]
        };
        var application = CreateApplication(
            getRules: instanceId => Result.Ok<EventRuleSet, DaemonError>(CreateRuleSet(instanceId, [rule])),
            sendCommand: async _ =>
            {
                actionEntered.TrySetResult();
                await releaseAction.Task;
                return Result.Ok<Unit, DaemonError>(Unit.Default);
            });
        await using var service = new EventTriggerService(
            application,
            port,
            new RecordingLogger<EventTriggerService>());

        await port.PublishAsync(new InstanceLogDomainEvent(Guid.NewGuid(), "ready"));
        await actionEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.StopAsync(cancellation.Token));

        releaseAction.TrySetResult();
        await service.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task OwnedTaskSupervisor_CancellationCallbackCanReenterSupervisor()
    {
        var supervisor = new OwnedTaskSupervisor("reentrant", new RecordingLogger<OwnedTaskSupervisor>());
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        supervisor.Schedule("wait", async token =>
        {
            using var registration = token.Register(() =>
            {
                _ = supervisor.PendingTaskCount;
            });
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));

        supervisor.RequestStop();
        await supervisor.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task OwnedTaskSupervisor_CancellationCallbackFailureIsObservedAfterTaskDrain()
    {
        var logger = new RecordingLogger<OwnedTaskSupervisor>();
        var supervisor = new OwnedTaskSupervisor("throwing", logger);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        supervisor.Schedule("wait", async token =>
        {
            using var registration = token.Register(
                () => throw new InvalidOperationException("cancel callback failed"));
            started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            finally
            {
                drained.TrySetResult();
            }
        });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));

        supervisor.RequestStop();
        var failure = await Assert.ThrowsAsync<AggregateException>(async () =>
            await supervisor.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)));

        await drained.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(0, supervisor.PendingTaskCount);
        Assert.Contains(failure.InnerExceptions, exception => exception is AggregateException);
        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Error && entry.Exception is AggregateException);
    }

    [Fact]
    public async Task RuleActionResultFailure_IsLoggedWithStructuredDaemonError()
    {
        var instanceId = Guid.NewGuid();
        var ruleId = Guid.NewGuid();
        var rules = new List<EventRule>
        {
            new()
            {
                Id = ruleId,
                IsEnabled = true,
                Triggers = [new ConsoleOutputTrigger { Pattern = "ready" }],
                Actions = [new SendCommandAction { Command = "say hello" }]
            }
        };
        var sendCalls = 0;
        var application = CreateApplication(
            getRules: id => Result.Ok<EventRuleSet, DaemonError>(CreateRuleSet(id, rules)),
            sendCommand: _ =>
            {
                sendCalls++;
                return Task.FromResult(Result.Err<Unit, DaemonError>(
                    new ConflictDaemonError("instance.not_running", "The instance is not running.")));
            });
        using var host = DomainEventPortTestHost.Create();
        var port = host.Port;
        var logger = new RecordingLogger<EventTriggerService>();
        await using var service = new EventTriggerService(application, port, logger);

        await port.PublishAsync(new InstanceLogDomainEvent(instanceId, "server ready"));

        Assert.Equal(1, sendCalls);
        var entry = Assert.Single(logger.Entries, entry =>
            entry.Properties.TryGetValue("ErrorCode", out var code) &&
            Equals(code, "instance.not_running"));
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(DaemonErrorKind.Conflict.ToString(), entry.Properties["ErrorKind"]);
        Assert.Equal(ruleId, entry.Properties["RuleId"]);
        Assert.Equal(instanceId, entry.Properties["InstanceId"]);
    }

    [Fact]
    public async Task MatchingRules_ExecuteSequentiallyAcrossRules()
    {
        var instanceId = Guid.NewGuid();
        var rules = new List<EventRule>
        {
            new()
            {
                IsEnabled = true,
                Triggers = [new ConsoleOutputTrigger { Pattern = "ready" }],
                Actions = [new SendCommandAction { Command = "first" }]
            },
            new()
            {
                IsEnabled = true,
                Triggers = [new ConsoleOutputTrigger { Pattern = "ready" }],
                Actions = [new SendCommandAction { Command = "second" }]
            }
        };
        var firstGate = new TaskCompletionSource<Result<Unit, DaemonError>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new List<string>();
        var application = CreateApplication(
            getRules: id => Result.Ok<EventRuleSet, DaemonError>(CreateRuleSet(id, rules)),
            sendCommand: request =>
            {
                calls.Add(request.Command);
                if (request.Command == "first")
                    return firstGate.Task;

                secondCalled.TrySetResult();
                return Task.FromResult(Result.Ok<Unit, DaemonError>(Unit.Default));
            });
        using var host = DomainEventPortTestHost.Create();
        var port = host.Port;
        await using var service = new EventTriggerService(
            application,
            port,
            new RecordingLogger<EventTriggerService>());

        await port.PublishAsync(new InstanceLogDomainEvent(instanceId, "server ready"));
        Assert.Equal(["first"], calls);

        firstGate.SetResult(Result.Ok<Unit, DaemonError>(Unit.Default));
        await secondCalled.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(["first", "second"], calls);
    }

    [Fact]
    public async Task LogTriggeredRestart_DoesNotSelfDeadlockTheProcessOutputPump()
    {
        var instanceId = Guid.NewGuid();
        var rule = new EventRule
        {
            IsEnabled = true,
            Triggers = [new ConsoleOutputTrigger { Pattern = "ready" }],
            Actions = [new ChangeInstanceStatusAction { Action = "restart" }]
        };
        using var process = new MCServerLauncher.Daemon.Management.Communicate.InstanceProcess(
            CreateReadyThenLongRunningStartInfo(),
            isMcServer: false);
        var restartCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var application = CreateApplication(
            getRules: id => Result.Ok<EventRuleSet, DaemonError>(CreateRuleSet(id, [rule])),
            startInstance: async (_, cancellationToken) =>
            {
                await process.WaitForExitAsync(cancellationToken);
                restartCompleted.TrySetResult();
                return Result.Ok<Unit, DaemonError>(Unit.Default);
            },
            stopInstance: (_, _) =>
            {
                process.KillProcess();
                return Task.FromResult(Result.Ok<Unit, DaemonError>(Unit.Default));
            });
        using var host = DomainEventPortTestHost.Create();
        var port = host.Port;
        await using var service = new EventTriggerService(
            application,
            port,
            new RecordingLogger<EventTriggerService>(),
            TimeSpan.FromMilliseconds(1));
        process.OnLog += (message, cancellationToken) =>
            port.PublishAsync(
                new InstanceLogDomainEvent(instanceId, message),
                cancellationToken).AsTask();

        var processStart = process.StartAsync(delayToCheck: 20);
        await restartCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await processStart.WaitAsync(TimeSpan.FromSeconds(5));
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(process.HasExit);
    }

    private static IDaemonApplication CreateApplication(
        Func<Guid, Result<EventRuleSet, DaemonError>> getRules,
        Func<InstanceCommandRequest, Task<Result<Unit, DaemonError>>>? sendCommand = null,
        Func<InstanceReference, CancellationToken, Task<Result<Unit, DaemonError>>>? startInstance = null,
        Func<InstanceReference, CancellationToken, Task<Result<Unit, DaemonError>>>? stopInstance = null)
    {
        var eventRules = new StubEventRuleApplication(getRules);
        var instances = CreateProxy<IInstanceApplication>((method, args) => method.Name switch
        {
            nameof(IInstanceApplication.SendCommandAsync) =>
                (sendCommand ?? (_ => Task.FromResult(Result.Ok<Unit, DaemonError>(Unit.Default))))(
                    Assert.IsType<InstanceCommandRequest>(args![0])),
            nameof(IInstanceApplication.StartInstanceAsync) =>
                (startInstance ?? ((_, _) => Task.FromResult(Result.Ok<Unit, DaemonError>(Unit.Default))))(
                    Assert.IsType<InstanceReference>(args![0]),
                    Assert.IsType<CancellationToken>(args[1])),
            nameof(IInstanceApplication.StopInstanceAsync) =>
                (stopInstance ?? ((_, _) => Task.FromResult(Result.Ok<Unit, DaemonError>(Unit.Default))))(
                    Assert.IsType<InstanceReference>(args![0]),
                    Assert.IsType<CancellationToken>(args[1])),
            _ => GetDefaultReturnValue(method.ReturnType)
        });
        return new StubDaemonApplication(instances, eventRules);
    }

    private static EventRuleSet CreateRuleSet(Guid instanceId, List<EventRule> rules)
    {
        var json = EventRuleDocumentCodec.SerializeToElement(rules);
        return new EventRuleSet(instanceId, json);
    }

    private static System.Diagnostics.ProcessStartInfo CreateReadyThenLongRunningStartInfo()
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true
        };
        if (OperatingSystem.IsWindows())
        {
            startInfo.ArgumentList.Add("/d");
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add("echo ready&set /p line=");
        }
        else
        {
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add("printf 'ready\\n'; read line");
        }

        return startInfo;
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

    private sealed class StubEventRuleApplication(Func<Guid, Result<EventRuleSet, DaemonError>> getRules)
        : IEventRuleApplication
    {
        public Task<Result<EventRuleSet, DaemonError>> GetEventRulesAsync(
            EventRuleQuery request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(getRules(request.InstanceId));
        }

        public Task<Result<Unit, DaemonError>> UpdateEventRulesAsync(
            EventRuleUpdateRequest request,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubDaemonApplication(
        IInstanceApplication instances,
        IEventRuleApplication eventRules) : IDaemonApplication
    {
        public IInstanceApplication Instances { get; } = instances;
        public IFileApplication Files => throw new NotSupportedException();
        public ISystemApplication System => throw new NotSupportedException();
        public IEventRuleApplication EventRules { get; } = eventRules;
    }

    private class InterfaceDispatchProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = (_, _) => null;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return Handler(targetMethod ?? throw new MissingMethodException("DispatchProxy target method was null."), args);
        }
    }

    private sealed class StepClock(params long[] timestamps) : IDomainEventClock
    {
        private int _index;

        public long GetTimestamp()
        {
            return timestamps[Math.Min(Interlocked.Increment(ref _index) - 1, timestamps.Length - 1)];
        }

        public TimeSpan GetElapsedTime(long startingTimestamp, long endingTimestamp)
        {
            return TimeSpan.FromMilliseconds(endingTimestamp - startingTimestamp);
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.Where(static pair => pair.Key != "{OriginalFormat}")
                    .ToDictionary(static pair => pair.Key, static pair => pair.Value)
                : new Dictionary<string, object?>();
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception, properties));
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        string Message,
        Exception? Exception,
        IReadOnlyDictionary<string, object?> Properties);
}
