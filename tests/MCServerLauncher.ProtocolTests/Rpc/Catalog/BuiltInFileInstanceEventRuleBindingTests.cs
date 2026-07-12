using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using MCServerLauncher.Common.Contracts.EventRules;
using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.Contracts.Serialization;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.Remote.Rpc.Catalog;
using RustyOptions;
using ContractInstanceReport = MCServerLauncher.Common.Contracts.Instances.InstanceReport;

namespace MCServerLauncher.ProtocolTests;

public sealed class BuiltInFileInstanceEventRuleBindingTests
{
    private static readonly ImmutableArray<string> FileMethods =
    [
        "mcsl.directory.copy", "mcsl.directory.create", "mcsl.directory.delete", "mcsl.directory.info.get",
        "mcsl.directory.move", "mcsl.directory.rename", "mcsl.file.copy", "mcsl.file.delete",
        "mcsl.file.download.close", "mcsl.file.download.open", "mcsl.file.download.read", "mcsl.file.info.get",
        "mcsl.file.move", "mcsl.file.rename", "mcsl.file.upload.cancel", "mcsl.file.upload.close",
        "mcsl.file.upload.open"
    ];

    private static readonly ImmutableArray<string> InstanceMethods =
    [
        "mcsl.instance.command.send", "mcsl.instance.create", "mcsl.instance.halt", "mcsl.instance.log.get",
        "mcsl.instance.remove", "mcsl.instance.report.get", "mcsl.instance.report.list",
        "mcsl.instance.settings.get", "mcsl.instance.settings.update", "mcsl.instance.start", "mcsl.instance.stop"
    ];

    private static readonly ImmutableArray<string> EventRuleMethods =
    ["mcsl.instance.event-rules.get", "mcsl.instance.event-rules.update"];

    [Fact]
    public void Registrars_RegisterExactDefinitionsOwnersAndGenericTypesWithoutFreezing()
    {
        var builder = CreateBuilder();
        BuiltInFileRpcRegistrar.Register(builder, new RecordingFileApplication());
        BuiltInInstanceRpcRegistrar.Register(builder, new RecordingInstanceApplication());
        BuiltInEventRuleRpcRegistrar.Register(builder, new RecordingEventRuleApplication());

        Assert.Null(builder.Catalog);
        var extraEvent = BuiltInProtocolDefinitions.Events.Single(
            descriptor => descriptor.Name.Value == "mcsl.event.daemon.report");
        builder.AddEventDefinition(ProtocolExecutionOwner.BuiltIn, extraEvent);

        var frozenBuilder = CreateBuilder();
        BuiltInFileRpcRegistrar.Register(frozenBuilder, new RecordingFileApplication());
        BuiltInInstanceRpcRegistrar.Register(frozenBuilder, new RecordingInstanceApplication());
        BuiltInEventRuleRpcRegistrar.Register(frozenBuilder, new RecordingEventRuleApplication());
        var catalog = frozenBuilder.Freeze();
        var expected = BuiltInProtocolDefinitions.Rpcs
            .Where(descriptor => AllMethods.Contains(descriptor.Method.Value, StringComparer.Ordinal))
            .ToArray();

        Assert.Equal(
            expected.Select(descriptor => descriptor.Method.Value).Order(StringComparer.Ordinal),
            catalog.Rpcs.Keys.Select(method => method.Value).Order(StringComparer.Ordinal));
        foreach (var descriptor in expected)
        {
            var entry = catalog.Rpcs[descriptor.Method];
            Assert.Same(descriptor, entry.Descriptor);
            Assert.Equal(ProtocolExecutionOwner.BuiltIn, entry.Owner);
            Assert.Equal(descriptor.RequestTypeInfo.Type, entry.Binding.RequestType);
            Assert.Equal(descriptor.ResultTypeInfo.Type, entry.Binding.ResultType);
        }
    }

    [Fact]
    public async Task FileBindings_DelegateEveryNonReadRpcExactlyOnceAndMapUnitResults()
    {
        var application = new RecordingFileApplication();
        var catalog = FreezeFile(application);
        using var cancellation = new CancellationTokenSource();
        var unitResults = new List<UnitResult>();
        var path = new PathRequest("a");
        var deleteDirectory = new DeleteDirectoryRequest("a", true);
        var transfer = new PathTransferRequest("a", "b");
        var rename = new PathRenameRequest("a", "b");
        var sessionId = Guid.NewGuid();
        var session = new FileSessionReference(sessionId);
        var uploadOpen = new UploadOpenRequest("a", 3, "hash");
        var downloadOpen = new DownloadOpenRequest("a");

        unitResults.Add(await InvokeUnit(catalog, "mcsl.directory.create", path, cancellation.Token));
        unitResults.Add(await InvokeUnit(catalog, "mcsl.directory.delete", deleteDirectory, cancellation.Token));
        Assert.Same(application.DirectoryDetails, await InvokeValue<PathRequest, DirectoryDetails>(catalog, "mcsl.directory.info.get", path, cancellation.Token));
        unitResults.Add(await InvokeUnit(catalog, "mcsl.directory.move", transfer, cancellation.Token));
        unitResults.Add(await InvokeUnit(catalog, "mcsl.directory.rename", rename, cancellation.Token));
        unitResults.Add(await InvokeUnit(catalog, "mcsl.directory.copy", transfer, cancellation.Token));
        unitResults.Add(await InvokeUnit(catalog, "mcsl.file.copy", transfer, cancellation.Token));
        unitResults.Add(await InvokeUnit(catalog, "mcsl.file.delete", path, cancellation.Token));
        unitResults.Add(await InvokeUnit(catalog, "mcsl.file.download.close", session, cancellation.Token));
        Assert.Same(application.DownloadSession, await InvokeValue<DownloadOpenRequest, DownloadSession>(catalog, "mcsl.file.download.open", downloadOpen, cancellation.Token));
        Assert.Same(application.FileDetails, await InvokeValue<PathRequest, FileDetails>(catalog, "mcsl.file.info.get", path, cancellation.Token));
        unitResults.Add(await InvokeUnit(catalog, "mcsl.file.move", transfer, cancellation.Token));
        unitResults.Add(await InvokeUnit(catalog, "mcsl.file.rename", rename, cancellation.Token));
        unitResults.Add(await InvokeUnit(catalog, "mcsl.file.upload.cancel", session, cancellation.Token));
        unitResults.Add(await InvokeUnit(catalog, "mcsl.file.upload.close", session, cancellation.Token));
        Assert.Same(application.UploadSession, await InvokeValue<UploadOpenRequest, UploadSession>(catalog, "mcsl.file.upload.open", uploadOpen, cancellation.Token));

        Assert.All(unitResults, AssertUnitJson);
        Assert.Equal(FileMethods.Where(method => method != "mcsl.file.download.read").Order(StringComparer.Ordinal), application.Calls.Select(call => call.Name).Order(StringComparer.Ordinal));
        Assert.All(application.Calls, call => Assert.Equal(cancellation.Token, call.Token));
        Assert.Equal(sessionId, application.Calls.Single(call => call.Name == "mcsl.file.download.close").Argument);
        Assert.Equal(sessionId, application.Calls.Single(call => call.Name == "mcsl.file.upload.cancel").Argument);
        Assert.Equal(sessionId, application.Calls.Single(call => call.Name == "mcsl.file.upload.close").Argument);
        Assert.Same(path, application.Calls.Single(call => call.Name == "mcsl.directory.create").Argument);
        Assert.Same(deleteDirectory, application.Calls.Single(call => call.Name == "mcsl.directory.delete").Argument);
        Assert.Same(path, application.Calls.Single(call => call.Name == "mcsl.directory.info.get").Argument);
        Assert.Same(transfer, application.Calls.Single(call => call.Name == "mcsl.directory.move").Argument);
        Assert.Same(rename, application.Calls.Single(call => call.Name == "mcsl.directory.rename").Argument);
        Assert.Same(transfer, application.Calls.Single(call => call.Name == "mcsl.directory.copy").Argument);
        Assert.Same(transfer, application.Calls.Single(call => call.Name == "mcsl.file.copy").Argument);
        Assert.Same(path, application.Calls.Single(call => call.Name == "mcsl.file.delete").Argument);
        Assert.Same(downloadOpen, application.Calls.Single(call => call.Name == "mcsl.file.download.open").Argument);
        Assert.Same(path, application.Calls.Single(call => call.Name == "mcsl.file.info.get").Argument);
        Assert.Same(transfer, application.Calls.Single(call => call.Name == "mcsl.file.move").Argument);
        Assert.Same(rename, application.Calls.Single(call => call.Name == "mcsl.file.rename").Argument);
        Assert.Same(uploadOpen, application.Calls.Single(call => call.Name == "mcsl.file.upload.open").Argument);
    }

    [Fact]
    public async Task DownloadRead_PreservesMetadataExactImmutableBytesErrorsAndCancellation()
    {
        var application = new RecordingFileApplication();
        var catalog = FreezeFile(application);
        var sessionId = Guid.NewGuid();
        var request = new DownloadChunkRequest(sessionId, 99, 16);
        var data = ImmutableArray.Create<byte>(1, 2, 3);
        application.DownloadChunk = new DownloadChunk(4, data, true);

        var success = await Invoke<DownloadChunkRequest, DownloadReadResult>(catalog, "mcsl.file.download.read", request, CancellationToken.None);

        Assert.True(success.Result.IsOk(out var metadata));
        Assert.Equal(sessionId, metadata.SessionId);
        Assert.NotEqual(request.Offset, metadata.Offset);
        Assert.Equal(application.DownloadChunk.Offset, metadata.Offset);
        Assert.Equal(data.Length, metadata.Length);
        Assert.True(metadata.IsFinal);
        var attachment = Assert.IsType<ProtocolDownloadAttachment>(success.DownloadAttachment);
        Assert.Equal(sessionId, attachment.SessionId);
        Assert.NotEqual(request.Offset, attachment.Offset);
        Assert.Equal(application.DownloadChunk.Offset, attachment.Offset);
        Assert.Equal(data, attachment.Data);
        Assert.True(attachment.Data == data);
        Assert.True(attachment.Data.AsSpan().SequenceEqual(data.AsSpan()));
        Assert.Equal(application.DownloadChunk.IsFinal, attachment.IsFinal);
        Assert.Same(request, application.Calls.Single().Argument);

        var expectedError = new StorageDaemonError("download.expected", "Expected failure.");
        application.Reset(errorMethod: "mcsl.file.download.read", error: expectedError);
        var failed = await Invoke<DownloadChunkRequest, DownloadReadResult>(catalog, "mcsl.file.download.read", request, CancellationToken.None);
        Assert.True(failed.Result.IsErr(out var actualError));
        Assert.Same(expectedError, actualError);
        Assert.Null(failed.DownloadAttachment);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        application.Reset(cancellationMethod: "mcsl.file.download.read");
        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            Invoke<DownloadChunkRequest, DownloadReadResult>(catalog, "mcsl.file.download.read", request, cancellation.Token));
        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task UnitBindingError_PreservesDaemonErrorIdentityAndHasNoAttachment()
    {
        var expectedError = new ConflictDaemonError("directory.expected", "Expected conflict.");
        var application = new RecordingFileApplication();
        application.Reset(errorMethod: "mcsl.directory.create", error: expectedError);
        var catalog = FreezeFile(application);

        var execution = await Invoke<PathRequest, UnitResult>(
            catalog,
            "mcsl.directory.create",
            new PathRequest("a"),
            CancellationToken.None);

        Assert.True(execution.Result.IsErr(out var actualError));
        Assert.Same(expectedError, actualError);
        Assert.Null(execution.DownloadAttachment);
    }

    [Fact]
    public async Task InstanceBindings_DelegateEveryRpcExactlyOnceAndMapUnitResults()
    {
        var application = new RecordingInstanceApplication();
        var catalog = FreezeInstance(application);
        using var cancellation = new CancellationTokenSource();
        var reference = new InstanceReference(application.InstanceId);
        var command = new InstanceCommandRequest(application.InstanceId, "say hi");
        var create = new CreateInstanceRequest(new InstanceFactoryConfiguration(application.Configuration, "source", SourceType.Core, InstanceFactoryMirror.None, false));
        var log = new InstanceLogQuery(application.InstanceId);
        var update = new UpdateInstanceSettingsRequest(application.InstanceId, "name", InstanceType.Universal, null, [], "1", null, false);
        var unitResults = new List<UnitResult>
        {
            await InvokeUnit(catalog, "mcsl.instance.command.send", command, cancellation.Token),
            await InvokeUnit(catalog, "mcsl.instance.halt", reference, cancellation.Token),
            await InvokeUnit(catalog, "mcsl.instance.remove", reference, cancellation.Token),
            await InvokeUnit(catalog, "mcsl.instance.start", reference, cancellation.Token),
            await InvokeUnit(catalog, "mcsl.instance.stop", reference, cancellation.Token)
        };
        Assert.Same(application.CreateResult, await InvokeValue<CreateInstanceRequest, CreateInstanceResult>(catalog, "mcsl.instance.create", create, cancellation.Token));
        Assert.Same(application.LogResult, await InvokeValue<InstanceLogQuery, InstanceLogResult>(catalog, "mcsl.instance.log.get", log, cancellation.Token));
        Assert.Same(application.Report, await InvokeValue<InstanceReference, ContractInstanceReport>(catalog, "mcsl.instance.report.get", reference, cancellation.Token));
        Assert.Same(application.ReportList, await InvokeValue<EmptyRequest, InstanceReportList>(catalog, "mcsl.instance.report.list", new EmptyRequest(), cancellation.Token));
        Assert.Same(application.Settings, await InvokeValue<InstanceReference, InstanceSettingsResult>(catalog, "mcsl.instance.settings.get", reference, cancellation.Token));
        Assert.Same(application.UpdateResult, await InvokeValue<UpdateInstanceSettingsRequest, UpdateInstanceSettingsResult>(catalog, "mcsl.instance.settings.update", update, cancellation.Token));

        Assert.All(unitResults, AssertUnitJson);
        Assert.Equal(InstanceMethods.Order(StringComparer.Ordinal), application.Calls.Select(call => call.Name).Order(StringComparer.Ordinal));
        Assert.All(application.Calls, call => Assert.Equal(cancellation.Token, call.Token));
        Assert.Same(command, application.Calls.Single(call => call.Name == "mcsl.instance.command.send").Argument);
        Assert.Same(create, application.Calls.Single(call => call.Name == "mcsl.instance.create").Argument);
        Assert.Same(reference, application.Calls.Single(call => call.Name == "mcsl.instance.halt").Argument);
        Assert.Same(log, application.Calls.Single(call => call.Name == "mcsl.instance.log.get").Argument);
        Assert.Same(reference, application.Calls.Single(call => call.Name == "mcsl.instance.remove").Argument);
        Assert.Same(reference, application.Calls.Single(call => call.Name == "mcsl.instance.report.get").Argument);
        Assert.Null(application.Calls.Single(call => call.Name == "mcsl.instance.report.list").Argument);
        Assert.Same(reference, application.Calls.Single(call => call.Name == "mcsl.instance.settings.get").Argument);
        Assert.Same(update, application.Calls.Single(call => call.Name == "mcsl.instance.settings.update").Argument);
        Assert.Same(reference, application.Calls.Single(call => call.Name == "mcsl.instance.start").Argument);
        Assert.Same(reference, application.Calls.Single(call => call.Name == "mcsl.instance.stop").Argument);
    }

    [Fact]
    public async Task EventRuleBindings_DelegateBothRpcsAndMapUnitResult()
    {
        var application = new RecordingEventRuleApplication();
        var catalog = FreezeEventRules(application);
        var query = new EventRuleQuery(application.InstanceId);
        var update = new EventRuleUpdateRequest(application.InstanceId, Json("{}"));
        using var cancellation = new CancellationTokenSource();

        Assert.Same(application.RuleSet, await InvokeValue<EventRuleQuery, EventRuleSet>(catalog, "mcsl.instance.event-rules.get", query, cancellation.Token));
        var unit = await InvokeUnit(catalog, "mcsl.instance.event-rules.update", update, cancellation.Token);

        AssertUnitJson(unit);
        Assert.Equal(EventRuleMethods, application.Calls.Select(call => call.Name));
        Assert.Same(query, application.Calls[0].Argument);
        Assert.Same(update, application.Calls[1].Argument);
        Assert.All(application.Calls, call => Assert.Equal(cancellation.Token, call.Token));
    }

    [Theory]
    [InlineData("file")]
    [InlineData("instance")]
    [InlineData("event-rule")]
    public async Task DomainRepresentativeErrorsPreserveIdentityAndCancellationPropagates(string domain)
    {
        var expectedError = new ConflictDaemonError("expected.conflict", "Expected conflict.");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        switch (domain)
        {
            case "file":
            {
                var application = new RecordingFileApplication();
                var catalog = FreezeFile(application);
                application.Reset(errorMethod: "mcsl.file.upload.open", error: expectedError);
                var failed = await Invoke<UploadOpenRequest, UploadSession>(catalog, "mcsl.file.upload.open", new UploadOpenRequest("a", 0, "hash"), CancellationToken.None);
                AssertSameError(expectedError, failed);
                application.Reset(cancellationMethod: "mcsl.directory.create");
                var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => Invoke<PathRequest, UnitResult>(catalog, "mcsl.directory.create", new PathRequest("a"), cancellation.Token));
                Assert.Equal(cancellation.Token, exception.CancellationToken);
                break;
            }
            case "instance":
            {
                var application = new RecordingInstanceApplication();
                var catalog = FreezeInstance(application);
                application.Reset(errorMethod: "mcsl.instance.create", error: expectedError);
                var request = new CreateInstanceRequest(new InstanceFactoryConfiguration(application.Configuration, "source", SourceType.Core, InstanceFactoryMirror.None, false));
                AssertSameError(expectedError, await Invoke<CreateInstanceRequest, CreateInstanceResult>(catalog, "mcsl.instance.create", request, CancellationToken.None));
                application.Reset(cancellationMethod: "mcsl.instance.halt");
                var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => Invoke<InstanceReference, UnitResult>(catalog, "mcsl.instance.halt", new InstanceReference(application.InstanceId), cancellation.Token));
                Assert.Equal(cancellation.Token, exception.CancellationToken);
                break;
            }
            case "event-rule":
            {
                var application = new RecordingEventRuleApplication();
                var catalog = FreezeEventRules(application);
                application.Reset(errorMethod: "mcsl.instance.event-rules.get", error: expectedError);
                AssertSameError(expectedError, await Invoke<EventRuleQuery, EventRuleSet>(catalog, "mcsl.instance.event-rules.get", new EventRuleQuery(application.InstanceId), CancellationToken.None));
                application.Reset(cancellationMethod: "mcsl.instance.event-rules.update");
                var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => Invoke<EventRuleUpdateRequest, UnitResult>(catalog, "mcsl.instance.event-rules.update", new EventRuleUpdateRequest(application.InstanceId, Json("{}")), cancellation.Token));
                Assert.Equal(cancellation.Token, exception.CancellationToken);
                break;
            }
        }
    }

    [Fact]
    public void FileCatalog_DoesNotRegisterBinaryUploadChunkOrPhaseFourConcepts()
    {
        var catalog = FreezeFile(new RecordingFileApplication());

        Assert.DoesNotContain(catalog.RpcDefinitions, descriptor => descriptor.Method.Value.Contains("chunk", StringComparison.Ordinal));
        Assert.DoesNotContain(catalog.RpcDefinitions, descriptor => descriptor.RequestTypeInfo.Type == typeof(UploadChunkRequest));
        var registrarTypes = new[] { typeof(BuiltInFileRpcRegistrar), typeof(BuiltInInstanceRpcRegistrar), typeof(BuiltInEventRuleRpcRegistrar), typeof(BuiltInApplicationRpcExecution) };
        var names = registrarTypes.SelectMany(type => type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)).Select(member => member.Name);
        Assert.DoesNotContain(names, name => name.Contains("ConnectionWriter", StringComparison.Ordinal) || name.Contains("Frame", StringComparison.Ordinal) || name.Contains("Acknowledgement", StringComparison.Ordinal));
        Assert.All(registrarTypes, type => Assert.DoesNotContain(type.GetFields(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic), field =>
            field.FieldType.FullName?.Contains("Manager", StringComparison.Ordinal) == true ||
            field.FieldType.FullName?.Contains("Storage", StringComparison.Ordinal) == true ||
            field.FieldType.FullName?.Contains("TouchSocket", StringComparison.Ordinal) == true ||
            field.FieldType.FullName?.Contains("WsContext", StringComparison.Ordinal) == true));
    }

    private static ImmutableArray<string> AllMethods => FileMethods.AddRange(InstanceMethods).AddRange(EventRuleMethods);

    private static ProtocolCatalogBuilder CreateBuilder() => new(new OpenRpcInfo("Binding tests", "1.0.0"));

    private static FrozenProtocolCatalog FreezeFile(RecordingFileApplication application)
    {
        var builder = CreateBuilder();
        BuiltInFileRpcRegistrar.Register(builder, application);
        return builder.Freeze();
    }

    private static FrozenProtocolCatalog FreezeInstance(RecordingInstanceApplication application)
    {
        var builder = CreateBuilder();
        BuiltInInstanceRpcRegistrar.Register(builder, application);
        return builder.Freeze();
    }

    private static FrozenProtocolCatalog FreezeEventRules(RecordingEventRuleApplication application)
    {
        var builder = CreateBuilder();
        BuiltInEventRuleRpcRegistrar.Register(builder, application);
        return builder.Freeze();
    }

    private static async Task<ProtocolRpcExecution<TResult>> Invoke<TRequest, TResult>(FrozenProtocolCatalog catalog, string method, TRequest request, CancellationToken token)
        where TResult : notnull
    {
        var binding = Assert.IsType<RpcBinding<TRequest, TResult>>(catalog.Rpcs[new RpcMethod(method)].Binding);
        return await binding.Handler(new ProtocolInvocationContext(ProtocolExecutionOwner.BuiltIn), request, token);
    }

    private static async Task<TResult> InvokeValue<TRequest, TResult>(FrozenProtocolCatalog catalog, string method, TRequest request, CancellationToken token)
        where TResult : notnull
    {
        var execution = await Invoke<TRequest, TResult>(catalog, method, request, token);
        Assert.True(execution.Result.IsOk(out var value));
        return value;
    }

    private static Task<UnitResult> InvokeUnit<TRequest>(FrozenProtocolCatalog catalog, string method, TRequest request, CancellationToken token) =>
        InvokeValue<TRequest, UnitResult>(catalog, method, request, token);

    private static void AssertUnitJson(UnitResult result) =>
        Assert.Equal("{}", JsonSerializer.Serialize(result, BuiltInProtocolJsonContext.Default.UnitResult));

    private static void AssertSameError<TResult>(DaemonError expected, ProtocolRpcExecution<TResult> execution)
        where TResult : notnull
    {
        Assert.True(execution.Result.IsErr(out var actual));
        Assert.Same(expected, actual);
        Assert.Null(execution.DownloadAttachment);
    }

    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();

    private sealed record Call(string Name, object? Argument, CancellationToken Token);

    private abstract class RecordingApplication
    {
        public List<Call> Calls { get; } = [];
        protected string? ErrorMethod { get; private set; }
        protected string? CancellationMethod { get; private set; }
        protected DaemonError? Error { get; private set; }

        public void Reset(string? errorMethod = null, DaemonError? error = null, string? cancellationMethod = null)
        {
            Calls.Clear();
            ErrorMethod = errorMethod;
            Error = error;
            CancellationMethod = cancellationMethod;
        }

        protected Task<Result<T, DaemonError>> Respond<T>(string name, object? argument, CancellationToken token, T value)
            where T : notnull
        {
            Calls.Add(new Call(name, argument, token));
            if (StringComparer.Ordinal.Equals(CancellationMethod, name))
                throw new OperationCanceledException(token);
            return Task.FromResult(StringComparer.Ordinal.Equals(ErrorMethod, name)
                ? Result.Err<T, DaemonError>(Error!)
                : Result.Ok<T, DaemonError>(value));
        }
    }

    private sealed class RecordingFileApplication : RecordingApplication, IFileApplication
    {
        private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
        public DirectoryDetails DirectoryDetails { get; } = new(null, [], []);
        public FileDetails FileDetails { get; } = new(new FileMetadata(Now, false, Now, Now, false, 1));
        public UploadSession UploadSession { get; } = new(Guid.NewGuid(), 1024, Now);
        public DownloadSession DownloadSession { get; } = new(Guid.NewGuid(), 3, "hash", 1024, Now);
        public DownloadChunk DownloadChunk { get; set; } = new(0, [], true);
        public Task<Result<DirectoryDetails, DaemonError>> GetDirectoryInfoAsync(PathRequest r, CancellationToken t) => Respond("mcsl.directory.info.get", r, t, DirectoryDetails);
        public Task<Result<FileDetails, DaemonError>> GetFileInfoAsync(PathRequest r, CancellationToken t) => Respond("mcsl.file.info.get", r, t, FileDetails);
        public Task<Result<Unit, DaemonError>> CreateDirectoryAsync(PathRequest r, CancellationToken t) => Respond("mcsl.directory.create", r, t, Unit.Default);
        public Task<Result<Unit, DaemonError>> DeleteFileAsync(PathRequest r, CancellationToken t) => Respond("mcsl.file.delete", r, t, Unit.Default);
        public Task<Result<Unit, DaemonError>> DeleteDirectoryAsync(DeleteDirectoryRequest r, CancellationToken t) => Respond("mcsl.directory.delete", r, t, Unit.Default);
        public Task<Result<Unit, DaemonError>> RenameFileAsync(PathRenameRequest r, CancellationToken t) => Respond("mcsl.file.rename", r, t, Unit.Default);
        public Task<Result<Unit, DaemonError>> RenameDirectoryAsync(PathRenameRequest r, CancellationToken t) => Respond("mcsl.directory.rename", r, t, Unit.Default);
        public Task<Result<Unit, DaemonError>> MoveFileAsync(PathTransferRequest r, CancellationToken t) => Respond("mcsl.file.move", r, t, Unit.Default);
        public Task<Result<Unit, DaemonError>> MoveDirectoryAsync(PathTransferRequest r, CancellationToken t) => Respond("mcsl.directory.move", r, t, Unit.Default);
        public Task<Result<Unit, DaemonError>> CopyFileAsync(PathTransferRequest r, CancellationToken t) => Respond("mcsl.file.copy", r, t, Unit.Default);
        public Task<Result<Unit, DaemonError>> CopyDirectoryAsync(PathTransferRequest r, CancellationToken t) => Respond("mcsl.directory.copy", r, t, Unit.Default);
        public Task<Result<UploadSession, DaemonError>> OpenUploadAsync(UploadOpenRequest r, CancellationToken t) => Respond("mcsl.file.upload.open", r, t, UploadSession);
        public Task<Result<Unit, DaemonError>> WriteUploadChunkAsync(UploadChunkRequest r, CancellationToken t) => throw new InvalidOperationException("Binary-only upload must not be called by RPC bindings.");
        public Task<Result<Unit, DaemonError>> CloseUploadAsync(Guid id, CancellationToken t) => Respond("mcsl.file.upload.close", id, t, Unit.Default);
        public Task<Result<Unit, DaemonError>> CancelUploadAsync(Guid id, CancellationToken t) => Respond("mcsl.file.upload.cancel", id, t, Unit.Default);
        public Task<Result<DownloadSession, DaemonError>> OpenDownloadAsync(DownloadOpenRequest r, CancellationToken t) => Respond("mcsl.file.download.open", r, t, DownloadSession);
        public Task<Result<DownloadChunk, DaemonError>> ReadDownloadChunkAsync(DownloadChunkRequest r, CancellationToken t) => Respond("mcsl.file.download.read", r, t, DownloadChunk);
        public Task<Result<Unit, DaemonError>> CloseDownloadAsync(Guid id, CancellationToken t) => Respond("mcsl.file.download.close", id, t, Unit.Default);
    }

    private sealed class RecordingInstanceApplication : RecordingApplication, IInstanceApplication
    {
        public Guid InstanceId { get; } = Guid.NewGuid();
        public InstanceConfiguration Configuration { get; }
        public CreateInstanceResult CreateResult { get; }
        public InstanceLogResult LogResult { get; } = new(["line"]);
        public ContractInstanceReport Report { get; }
        public InstanceReportList ReportList { get; }
        public InstanceSettingsResult Settings { get; }
        public UpdateInstanceSettingsResult UpdateResult { get; }

        public RecordingInstanceApplication()
        {
            Configuration = new InstanceConfiguration(InstanceId, "name", "target", InstanceType.Universal, TargetType.Executable, "1", "utf-8", "utf-8", "java", [], ImmutableDictionary<string, string>.Empty, Json("{}"));
            CreateResult = new CreateInstanceResult(Configuration);
            Report = new ContractInstanceReport(InstanceStatus.Stopped, Configuration, ImmutableDictionary<string, string>.Empty, [], new InstancePerformance(0, 0), null);
            ReportList = new InstanceReportList(ImmutableDictionary<Guid, ContractInstanceReport>.Empty.Add(InstanceId, Report));
            Settings = new InstanceSettingsResult(Configuration, "work", true, true, null, null);
            UpdateResult = new UpdateInstanceSettingsResult(Configuration, false, false, [], []);
        }

        public Task<Result<CreateInstanceResult, DaemonError>> CreateInstanceAsync(CreateInstanceRequest r, CancellationToken t) => Respond("mcsl.instance.create", r, t, CreateResult);
        public Task<Result<Unit, DaemonError>> RemoveInstanceAsync(InstanceReference r, CancellationToken t) => Respond("mcsl.instance.remove", r, t, Unit.Default);
        public Task<Result<Unit, DaemonError>> StartInstanceAsync(InstanceReference r, CancellationToken t) => Respond("mcsl.instance.start", r, t, Unit.Default);
        public Task<Result<Unit, DaemonError>> StopInstanceAsync(InstanceReference r, CancellationToken t) => Respond("mcsl.instance.stop", r, t, Unit.Default);
        public Task<Result<Unit, DaemonError>> HaltInstanceAsync(InstanceReference r, CancellationToken t) => Respond("mcsl.instance.halt", r, t, Unit.Default);
        public Task<Result<Unit, DaemonError>> SendCommandAsync(InstanceCommandRequest r, CancellationToken t) => Respond("mcsl.instance.command.send", r, t, Unit.Default);
        public Task<Result<ContractInstanceReport, DaemonError>> GetInstanceReportAsync(InstanceReference r, CancellationToken t) => Respond("mcsl.instance.report.get", r, t, Report);
        public Task<Result<InstanceReportList, DaemonError>> ListInstanceReportsAsync(CancellationToken t) => Respond("mcsl.instance.report.list", null, t, ReportList);
        public Task<Result<InstanceLogResult, DaemonError>> GetInstanceLogAsync(InstanceLogQuery r, CancellationToken t) => Respond("mcsl.instance.log.get", r, t, LogResult);
        public Task<Result<InstanceSettingsResult, DaemonError>> GetInstanceSettingsAsync(InstanceReference r, CancellationToken t) => Respond("mcsl.instance.settings.get", r, t, Settings);
        public Task<Result<UpdateInstanceSettingsResult, DaemonError>> UpdateInstanceSettingsAsync(UpdateInstanceSettingsRequest r, CancellationToken t) => Respond("mcsl.instance.settings.update", r, t, UpdateResult);
    }

    private sealed class RecordingEventRuleApplication : RecordingApplication, IEventRuleApplication
    {
        public Guid InstanceId { get; } = Guid.NewGuid();
        public EventRuleSet RuleSet { get; }
        public RecordingEventRuleApplication() => RuleSet = new EventRuleSet(InstanceId, Json("{}"));
        public Task<Result<EventRuleSet, DaemonError>> GetEventRulesAsync(EventRuleQuery r, CancellationToken t) => Respond("mcsl.instance.event-rules.get", r, t, RuleSet);
        public Task<Result<Unit, DaemonError>> UpdateEventRulesAsync(EventRuleUpdateRequest r, CancellationToken t) => Respond("mcsl.instance.event-rules.update", r, t, Unit.Default);
    }
}
