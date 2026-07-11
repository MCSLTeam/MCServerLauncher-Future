using System.Reflection;
using System.Text.Json;
using MCServerLauncher.Common.Contracts.EventRules;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.ProtoType.EventTrigger;
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
    public void Publish_IsolatesSubscriberExceptionsAndContinuesDispatch()
    {
        var logger = new RecordingLogger<DomainEventPort>();
        var port = new DomainEventPort(logger);
        var delivered = 0;
        using var failing = port.Subscribe<InstanceLogDomainEvent>(_ => throw new InvalidOperationException("subscriber failed"));
        using var succeeding = port.Subscribe<InstanceLogDomainEvent>(_ => delivered++);

        var exception = Record.Exception(() => port.Publish(new InstanceLogDomainEvent(Guid.NewGuid(), "ready")));

        Assert.Null(exception);
        Assert.Equal(1, delivered);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.IsType<InvalidOperationException>(entry.Exception);
        Assert.Contains(nameof(InstanceLogDomainEvent), entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SubscriptionAndTriggerLifecycle_AreIdempotentAndStopNewDelivery()
    {
        var port = new DomainEventPort(new RecordingLogger<DomainEventPort>());
        var directDeliveries = 0;
        var subscription = port.Subscribe<InstanceLogDomainEvent>(_ => directDeliveries++);
        port.Publish(new InstanceLogDomainEvent(Guid.NewGuid(), "first"));
        subscription.Dispose();
        subscription.Dispose();
        port.Publish(new InstanceLogDomainEvent(Guid.NewGuid(), "second"));
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

        port.Publish(new InstanceLogDomainEvent(targetId, "running"));
        Assert.Equal(1, ruleReads);

        service.Stop();
        service.Stop();
        port.Publish(new InstanceLogDomainEvent(targetId, "stopped"));
        Assert.Equal(1, ruleReads);

        service.Start();
        service.Start();
        port.Publish(new InstanceLogDomainEvent(targetId, "restarted"));
        Assert.Equal(2, ruleReads);

        service.Dispose();
        service.Dispose();
        port.Publish(new InstanceLogDomainEvent(targetId, "disposed"));
        Assert.Equal(2, ruleReads);
        Assert.Throws<ObjectDisposedException>(service.Start);
    }

    [Fact]
    public void RuleActionResultFailure_IsLoggedWithStructuredDaemonError()
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
        var port = new DomainEventPort(new RecordingLogger<DomainEventPort>());
        var logger = new RecordingLogger<EventTriggerService>();
        using var service = new EventTriggerService(application, port, logger);

        port.Publish(new InstanceLogDomainEvent(instanceId, "server ready"));

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
        var port = new DomainEventPort(new RecordingLogger<DomainEventPort>());
        using var service = new EventTriggerService(
            application,
            port,
            new RecordingLogger<EventTriggerService>());

        port.Publish(new InstanceLogDomainEvent(instanceId, "server ready"));
        Assert.Equal(["first"], calls);

        firstGate.SetResult(Result.Ok<Unit, DaemonError>(Unit.Default));
        await secondCalled.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(["first", "second"], calls);
    }

    private static IDaemonApplication CreateApplication(
        Func<Guid, Result<EventRuleSet, DaemonError>> getRules,
        Func<InstanceCommandRequest, Task<Result<Unit, DaemonError>>>? sendCommand = null)
    {
        var eventRules = new StubEventRuleApplication(getRules);
        var instances = CreateProxy<IInstanceApplication>((method, args) => method.Name switch
        {
            nameof(IInstanceApplication.SendCommandAsync) =>
                (sendCommand ?? (_ => Task.FromResult(Result.Ok<Unit, DaemonError>(Unit.Default))))(
                    Assert.IsType<InstanceCommandRequest>(args![0])),
            _ => GetDefaultReturnValue(method.ReturnType)
        });
        return new StubDaemonApplication(instances, eventRules);
    }

    private static EventRuleSet CreateRuleSet(Guid instanceId, List<EventRule> rules)
    {
        var json = JsonSerializer.SerializeToElement(rules, EventRuleJsonContext.Default.EventRuleList);
        return new EventRuleSet(instanceId, json);
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

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

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
