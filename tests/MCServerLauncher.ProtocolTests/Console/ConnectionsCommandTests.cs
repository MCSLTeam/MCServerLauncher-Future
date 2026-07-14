using System.Collections.Immutable;
using System.Reflection;
using Brigadier.NET;
using MCServerLauncher.Daemon.Console;
using MCServerLauncher.Daemon.Console.Commands;
using MCServerLauncher.Daemon.Remote.Rpc.Transport;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using TouchSocket.Core.AspNetCore;
using TouchSocket.Http;

namespace MCServerLauncher.ProtocolTests;

[Collection(ConnectionsCommandLogTestCollection.Name)]
public sealed class ConnectionsCommandTests
{
    [Fact]
    public void ExpireAll_FaultedAdministrationReturnsFailureAndHidesExceptionDetails()
    {
        var failure = new InvalidOperationException("physical close failure must remain private");
        var source = CreateSource(new FaultingConnectionAdministration(failure));
        var dispatcher = new CommandDispatcher<ConsoleCommandSource>();
        ConnectionsCommand.Register(dispatcher);
        var sink = new RecordingSink();
        var originalLogger = Log.Logger;
        using var testLogger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Sink(sink)
            .CreateLogger();

        try
        {
            Log.Logger = testLogger;

            var exitCode = 0;
            var exception = Record.Exception(() => exitCode = dispatcher.Execute("conn expire_all", source));

            Assert.Null(exception);
            Assert.Equal(1, exitCode);

            var consoleError = Assert.Single(sink.Events, logEvent =>
                logEvent.MessageTemplate.Text == "[Console] 已过期凭据；关闭 V2 WebSocket 客户端连接时发生错误。");
            Assert.Null(consoleError.Exception);
            Assert.DoesNotContain(failure.Message, consoleError.RenderMessage(), StringComparison.Ordinal);

            var diagnostic = Assert.Single(sink.Events, logEvent =>
                logEvent.MessageTemplate.Text ==
                "[ConnectionsCommand] Failed to close V2 WebSocket client connections after expiring credentials.");
            Assert.Same(failure, diagnostic.Exception);
        }
        finally
        {
            Log.Logger = originalLogger;
        }
    }

    private static ConsoleCommandSource CreateSource(IV2ConnectionAdministration connections)
    {
        var services = new ServiceCollection();
        services.AddSingleton(connections);
        var resolver = new AspNetCoreContainer(services).BuildResolver();
        var httpService = CreateProxy<IHttpService>((method, _) => method.Name == "get_Resolver"
            ? resolver
            : GetDefaultReturnValue(method.ReturnType));

        return new ConsoleCommandSource(httpService);
    }

    private static T CreateProxy<T>(Func<MethodInfo, object?[]?, object?> handler)
        where T : class
    {
        var proxy = DispatchProxy.Create<T, InterfaceDispatchProxy>();
        ((InterfaceDispatchProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    private static object? GetDefaultReturnValue(Type returnType) =>
        returnType == typeof(void)
            ? null
            : returnType.IsValueType
                ? Activator.CreateInstance(returnType)
                : null;

    private sealed class FaultingConnectionAdministration(Exception exception) : IV2ConnectionAdministration
    {
        public ImmutableArray<V2ConnectionSnapshot> Snapshot() => ImmutableArray<V2ConnectionSnapshot>.Empty;

        public bool TryGet(string connectionId, out V2ConnectionSnapshot connection)
        {
            connection = default;
            return false;
        }

        public Task<bool> CloseAsync(string connectionId) => Task.FromResult(false);

        public Task<int> CloseAllAsync() => Task.FromException<int>(exception);

        public Task ShutdownAsync() => Task.CompletedTask;
    }

    private sealed class RecordingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent)
        {
            Events.Add(logEvent);
        }
    }

    private class InterfaceDispatchProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = (_, _) => null;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return Handler(targetMethod ?? throw new MissingMethodException("DispatchProxy target method was null."), args);
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConnectionsCommandLogTestCollection
{
    public const string Name = "Connections command log isolated tests";
}
