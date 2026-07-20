using System.Reflection;
using System.Text.Json.Serialization.Metadata;
using System.Diagnostics.CodeAnalysis;
using MCServerLauncher.Common.Contracts.EventRules;
using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.Contracts.System;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.API.State;
using MCServerLauncher.Daemon.Remote.Rpc.Catalog;
using RustyOptions;

namespace MCServerLauncher.ProtocolTests;

public sealed class BuiltInProtocolCatalogCompositionTests
{
    [Fact]
    public void Composition_FreezesAndPublishesTheDefinitionDerivedBuiltInCatalog()
    {
        var accessor = new FrozenProtocolCatalogAccessor();
        var composition = CreateComposition(accessor);

        Assert.Same(composition.Catalog, accessor.GetRequired());
        Assert.Equal(BuiltInProtocolDefinitions.Rpcs.Length, composition.Catalog.Rpcs.Count);
        Assert.Equal(BuiltInProtocolDefinitions.Events.Length, composition.Catalog.Events.Count);

        foreach (var descriptor in BuiltInProtocolDefinitions.Rpcs)
        {
            var frozen = composition.Catalog.Rpcs[descriptor.Method];
            Assert.Same(ProtocolExecutionOwner.BuiltIn, frozen.Owner);
            Assert.Same(descriptor, frozen.Descriptor);
            Assert.Same(frozen.Owner, frozen.Binding.Owner);
            Assert.Equal(descriptor.RequestTypeInfo.Type, frozen.Binding.RequestType);
            Assert.Equal(descriptor.ResultTypeInfo.Type, frozen.Binding.ResultType);
            Assert.Same(descriptor.RequestTypeInfo, frozen.Descriptor.RequestTypeInfo);
            Assert.Same(descriptor.ResultTypeInfo, frozen.Descriptor.ResultTypeInfo);

            var bindingType = typeof(RpcBinding<,>).MakeGenericType(
                descriptor.RequestTypeInfo.Type,
                descriptor.ResultTypeInfo.Type);
            Assert.IsType(bindingType, frozen.Binding);
        }

        foreach (var descriptor in BuiltInProtocolDefinitions.Events)
        {
            var frozen = composition.Catalog.Events[descriptor.Name];
            Assert.Same(ProtocolExecutionOwner.BuiltIn, frozen.Owner);
            Assert.Same(descriptor, frozen.Descriptor);
            Assert.Same(frozen.Owner, frozen.Binding.Owner);
            Assert.Equal(descriptor.DataTypeInfo.Type, frozen.Binding.DataType);
            Assert.Equal(descriptor.MetaTypeInfo?.Type, frozen.Binding.MetaType);
            Assert.Same(descriptor.DataTypeInfo, frozen.Descriptor.DataTypeInfo);
            Assert.Same(descriptor.MetaTypeInfo, frozen.Descriptor.MetaTypeInfo);
        }
    }

    [Fact]
    public async Task Composition_PluginDraftIsFrozenBeforePublicationAndDiscoverUsesTheFinalDocument()
    {
        var accessor = new FrozenProtocolCatalogAccessor();
        Assert.False(accessor.TryGet(out _));
        Assert.Throws<InvalidOperationException>(accessor.GetRequired);

        ProtocolCatalogBuilder? draft = null;
        var pluginDescriptor = PluginPingDescriptor();
        var pluginOwner = ProtocolExecutionOwner.ForPlugin(new ProtocolOwnerIdentity("test.health", "1.0.0"));
        var composition = CreateComposition(accessor, builder =>
        {
            draft = builder;
            builder.AddRpcDefinition(pluginOwner, pluginDescriptor);
            builder.AddRpcBinding(
                pluginDescriptor.Method,
                new RpcBinding<EmptyRequest, PingResult>(
                    pluginOwner,
                    static (_, _, _) => Task.FromResult(
                        ProtocolRpcExecution<PingResult>.Ok(new PingResult(1)))));
        });

        Assert.NotNull(draft);
        Assert.Same(composition.Catalog, draft.Catalog);
        Assert.Throws<InvalidOperationException>(() => draft.AddRpcDefinition(pluginOwner, pluginDescriptor));
        Assert.Throws<InvalidOperationException>(() => accessor.Publish(composition.Catalog));
        Assert.Same(pluginDescriptor, composition.Catalog.Rpcs[pluginDescriptor.Method].Descriptor);

        var discoverDescriptor = BuiltInProtocolDefinitions.Rpcs.Single(candidate => candidate.Method.Value == "rpc.discover");
        var discover = composition.Catalog.Rpcs[discoverDescriptor.Method];
        var binding = Assert.IsType<RpcBinding<EmptyRequest, OpenRpcDocument>>(discover.Binding);
        var execution = await binding.Handler(
            new ProtocolInvocationContext(ProtocolExecutionOwner.BuiltIn),
            new EmptyRequest(),
            CancellationToken.None);

        Assert.True(execution.Result.IsOk(out var document));
        Assert.Same(composition.Catalog.Document, document);
        Assert.Contains(document!.Methods, method => method.Name == pluginDescriptor.Method.Value);
    }

    [Fact]
    public void Composition_DraftFailureLeavesTheAccessorUnpublished()
    {
        var accessor = new FrozenProtocolCatalogAccessor();
        var expected = new InvalidOperationException("draft failed");

        var actual = Assert.Throws<InvalidOperationException>(() => CreateComposition(
            accessor,
            _ => throw expected));

        Assert.Same(expected, actual);
        Assert.False(accessor.TryGet(out _));
    }

    private static BuiltInProtocolCatalogComposition CreateComposition(
        FrozenProtocolCatalogAccessor accessor,
        Action<ProtocolCatalogBuilder>? configureDraft = null) =>
        new(
            new ThrowingInstanceApplication(),
            new ThrowingFileApplication(),
            new ThrowingSystemApplication(),
            new ThrowingEventRuleApplication(),
            new EmptySnapshotSource(),
            TimeProvider.System,
            accessor,
            configureDraft);

    private static RpcDescriptor<EmptyRequest, PingResult> PluginPingDescriptor()
    {
        var builtIn = (RpcDescriptor<EmptyRequest, PingResult>)BuiltInProtocolDefinitions.Rpcs.Single(
            descriptor => descriptor.Method.Value == "mcsl.daemon.ping");
        var constructor = typeof(RpcDescriptor<EmptyRequest, PingResult>).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            [typeof(RpcMethod), typeof(PermissionName), typeof(JsonTypeInfo<EmptyRequest>), typeof(JsonTypeInfo<PingResult>), typeof(bool), typeof(RpcDocumentation)],
            null)!;
        return (RpcDescriptor<EmptyRequest, PingResult>)constructor.Invoke(
        [
            new RpcMethod("plugin.test.health.rpc.ping"),
            new PermissionName("*"),
            builtIn.RequestTypeInfo,
            builtIn.ResultTypeInfo,
            false,
            new RpcDocumentation("test", "Plugin ping", "Synthetic plugin method.", "plugin.ping.request", "plugin.ping.result")
        ]);
    }

    private sealed class EmptySnapshotSource : IInstanceSnapshotSource
    {
        private readonly StatePublisher<InstanceCatalogSnapshot> _publisher = new(InstanceCatalogSnapshot.Empty);
        public PublishedState<InstanceCatalogSnapshot> Current => _publisher.Current;
        public bool TryGet(Guid instanceId, [NotNullWhen(true)] out InstanceSnapshot? snapshot) => Current.Value.TryGet(instanceId, out snapshot);
    }

    private sealed class ThrowingSystemApplication : ISystemApplication
    {
        public Task<Result<SystemInfo, DaemonError>> GetSystemInfoAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<JavaRuntimeList, DaemonError>> ListJavaRuntimesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ThrowingEventRuleApplication : IEventRuleApplication
    {
        public Task<Result<EventRuleSet, DaemonError>> GetEventRulesAsync(EventRuleQuery request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> UpdateEventRulesAsync(EventRuleUpdateRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ThrowingInstanceApplication : IInstanceApplication
    {
        public Task<Result<CreateInstanceResult, DaemonError>> CreateInstanceAsync(CreateInstanceRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> RemoveInstanceAsync(InstanceReference request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> StartInstanceAsync(InstanceReference request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> StopInstanceAsync(InstanceReference request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> HaltInstanceAsync(InstanceReference request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> SendCommandAsync(InstanceCommandRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<ConsoleSession, DaemonError>> OpenConsoleAsync(ConsoleOpenRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> ResizeConsoleAsync(ConsoleResizeRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> CloseConsoleAsync(ConsoleSessionReference request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> WriteConsoleAsync(Guid sessionId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<InstanceReport, DaemonError>> GetInstanceReportAsync(InstanceReference request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<InstanceReportList, DaemonError>> ListInstanceReportsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<InstanceLogResult, DaemonError>> GetInstanceLogAsync(InstanceLogQuery request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<InstanceSettingsResult, DaemonError>> GetInstanceSettingsAsync(InstanceReference request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<UpdateInstanceSettingsResult, DaemonError>> UpdateInstanceSettingsAsync(UpdateInstanceSettingsRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ThrowingFileApplication : IFileApplication
    {
        public Task<Result<DirectoryDetails, DaemonError>> GetDirectoryInfoAsync(PathRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<FileDetails, DaemonError>> GetFileInfoAsync(PathRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> CreateDirectoryAsync(PathRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> DeleteFileAsync(PathRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> DeleteDirectoryAsync(DeleteDirectoryRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> RenameFileAsync(PathRenameRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> RenameDirectoryAsync(PathRenameRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> MoveFileAsync(PathTransferRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> MoveDirectoryAsync(PathTransferRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> CopyFileAsync(PathTransferRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> CopyDirectoryAsync(PathTransferRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<UploadSession, DaemonError>> OpenUploadAsync(UploadOpenRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> WriteUploadChunkAsync(UploadChunkRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> CloseUploadAsync(Guid sessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> CancelUploadAsync(Guid sessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<DownloadSession, DaemonError>> OpenDownloadAsync(DownloadOpenRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<DownloadChunk, DaemonError>> ReadDownloadChunkAsync(DownloadChunkRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> CloseDownloadAsync(Guid sessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

}
