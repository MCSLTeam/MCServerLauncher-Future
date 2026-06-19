using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Daemon.Remote;
using MCServerLauncher.Daemon.Remote.Action;
using RustyOptions;
using TouchSocket.Core;

namespace MCServerLauncher.ProtocolTests;

[Collection("LegacyActionRegistryIsolation")]
public class ActionFailureParityCharacterizationTests
{
    private static readonly Guid FixedRequestId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    [Fact]
    [Trait("Category", "LegacyActionRegistry")]
    public async Task LegacyMalformedJson_ProcessAction_ReturnsBadRequestCouldNotParseActionJson()
    {
        var resolver = LegacyActionRegistryHarness.CreateResolver();
        var executor = LegacyActionRegistryHarness.CreateProductionExecutor(resolver);

        try
        {
            var response = executor.ProcessAction("{\"action\":\"ping\"", LegacyActionRegistryHarness.CreateContext());

            Assert.NotNull(response);
            Assert.Equal(ActionRequestStatus.Error, response!.RequestStatus);
            Assert.Equal(ActionRetcode.BadRequest.Code, response.Retcode);
            Assert.Equal("Bad Request: Could not parse action json", response.Message);
            Assert.Equal(Guid.Empty, response.Id);
            Assert.Null(response.Data);
        }
        finally
        {
            await LegacyActionRegistryHarness.ShutdownExecutorAsync(executor);
        }
    }

    [Fact]
    [Trait("Category", "LegacyActionRegistry")]
    public async Task LegacyUnknownActionString_ProcessAction_ReturnsBadRequestCouldNotParseActionJson()
    {
        var resolver = LegacyActionRegistryHarness.CreateResolver();
        var executor = LegacyActionRegistryHarness.CreateProductionExecutor(resolver);
        var json =
            $$"""
            {
              "action": "does_not_exist",
              "params": {},
              "id": "{{FixedRequestId}}"
            }
            """;

        try
        {
            var response = executor.ProcessAction(json, LegacyActionRegistryHarness.CreateContext());

            Assert.NotNull(response);
            Assert.Equal(ActionRequestStatus.Error, response!.RequestStatus);
            Assert.Equal(ActionRetcode.BadRequest.Code, response.Retcode);
            Assert.Equal("Bad Request: Could not parse action json", response.Message);
            Assert.Equal(Guid.Empty, response.Id);
            Assert.Null(response.Data);
        }
        finally
        {
            await LegacyActionRegistryHarness.ShutdownExecutorAsync(executor);
        }
    }

    [Fact]
    [Trait("Category", "LegacyActionRegistry")]
    public async Task LegacyMissingParamsEnvelope_ProcessAction_ReturnsBadRequestCouldNotParseActionJson()
    {
        var resolver = LegacyActionRegistryHarness.CreateResolver();
        var executor = LegacyActionRegistryHarness.CreateProductionExecutor(resolver);
        var json =
            $$"""
            {
              "action": "ping",
              "id": "{{FixedRequestId}}"
            }
            """;

        try
        {
            var response = executor.ProcessAction(json, LegacyActionRegistryHarness.CreateContext());

            Assert.NotNull(response);
            Assert.Equal(ActionRequestStatus.Error, response!.RequestStatus);
            Assert.Equal(ActionRetcode.BadRequest.Code, response.Retcode);
            Assert.Equal("Bad Request: Could not parse action json", response.Message);
            Assert.Equal(Guid.Empty, response.Id);
            Assert.Null(response.Data);
        }
        finally
        {
            await LegacyActionRegistryHarness.ShutdownExecutorAsync(executor);
        }
    }

    [Fact]
    [Trait("Category", "LegacyActionRegistry")]
    public async Task LegacyNullParamsEnvelope_ProcessAction_ReturnsParamErrorMissingParameters()
    {
        var resolver = LegacyActionRegistryHarness.CreateResolver();
        var executor = LegacyActionRegistryHarness.CreateProductionExecutor(resolver);
        var json =
            $$"""
            {
              "action": "ping",
              "params": null,
              "id": "{{FixedRequestId}}"
            }
            """;

        try
        {
            var response = executor.ProcessAction(json, LegacyActionRegistryHarness.CreateContext());

            Assert.NotNull(response);
            Assert.Equal(ActionRequestStatus.Error, response!.RequestStatus);
            Assert.Equal(ActionRetcode.ParamError.Code, response.Retcode);
            Assert.Equal(LegacyActionRegistryHarness.FormatActionErrorMessage("Param Error: Missing parameters"),
                response.Message);
            Assert.Equal(FixedRequestId, response.Id);
            Assert.Null(response.Data);
        }
        finally
        {
            await LegacyActionRegistryHarness.ShutdownExecutorAsync(executor);
        }
    }

    [Fact]
    [Trait("Category", "LegacyActionRegistry")]
    public void LegacySyncDelegate_ExceptionBehavior_BubblesHandlerException()
    {
        var snapshot = LegacyActionRegistryHarness.BuildSnapshot(typeof(ThrowingSyncProbeHandler));
        var resolver = LegacyActionRegistryHarness.CreateResolver();
        var context = LegacyActionRegistryHarness.CreateContext();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            snapshot.SyncHandlers[ActionType.Ping].Invoke(
                LegacyActionRegistryHarness.ParseElement("{}"),
                FixedRequestId,
                context,
                resolver,
                CancellationToken.None));

        Assert.Equal("sync-probe exploded", exception.Message);
    }

    [Fact]
    [Trait("Category", "LegacyActionRegistry")]
    public async Task LegacyAsyncDelegate_ExceptionBehavior_BubblesHandlerException()
    {
        var snapshot = LegacyActionRegistryHarness.BuildSnapshot(typeof(ThrowingAsyncProbeHandler));
        var resolver = LegacyActionRegistryHarness.CreateResolver();
        var context = LegacyActionRegistryHarness.CreateContext();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await snapshot.AsyncHandlers[ActionType.GetSystemInfo].Invoke(
                LegacyActionRegistryHarness.ParseElement("{}"),
                FixedRequestId,
                context,
                resolver,
                CancellationToken.None));

        Assert.Equal("async-probe exploded", exception.Message);
    }

    [Fact]
    [Trait("Category", "LegacyActionRegistry")]
    public void LegacySyncDelegate_CancellationBehavior_PropagatesOperationCanceledException()
    {
        var snapshot = LegacyActionRegistryHarness.BuildSnapshot(typeof(CancelingSyncProbeHandler));
        var resolver = LegacyActionRegistryHarness.CreateResolver();
        var context = LegacyActionRegistryHarness.CreateContext();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var exception = Assert.Throws<OperationCanceledException>(() =>
            snapshot.SyncHandlers[ActionType.Ping].Invoke(
                LegacyActionRegistryHarness.ParseElement("{}"),
                FixedRequestId,
                context,
                resolver,
                cts.Token));

        Assert.Equal(cts.Token, exception.CancellationToken);
    }

    [Fact]
    [Trait("Category", "LegacyActionRegistry")]
    public async Task LegacyAsyncDelegate_CancellationBehavior_PropagatesOperationCanceledException()
    {
        var snapshot = LegacyActionRegistryHarness.BuildSnapshot(typeof(CancelingAsyncProbeHandler));
        var resolver = LegacyActionRegistryHarness.CreateResolver();
        var context = LegacyActionRegistryHarness.CreateContext();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await snapshot.AsyncHandlers[ActionType.GetSystemInfo].Invoke(
                LegacyActionRegistryHarness.ParseElement("{}"),
                FixedRequestId,
                context,
                resolver,
                cts.Token));

        Assert.Equal(cts.Token, exception.CancellationToken);
    }

    [ActionHandler(ActionType.Ping, "*")]
    public sealed class ThrowingSyncProbeHandler : IActionHandler<EmptyActionParameter, EmptyActionResult>
    {
        public Result<EmptyActionResult, ActionError> Handle(
            EmptyActionParameter param,
            WsContext ctx,
            IResolver resolver,
            CancellationToken ct)
        {
            throw new InvalidOperationException("sync-probe exploded");
        }
    }

    [ActionHandler(ActionType.GetSystemInfo, "*")]
    public sealed class ThrowingAsyncProbeHandler : IAsyncActionHandler<EmptyActionParameter, EmptyActionResult>
    {
        public async Task<Result<EmptyActionResult, ActionError>> HandleAsync(
            EmptyActionParameter param,
            WsContext ctx,
            IResolver resolver,
            CancellationToken ct)
        {
            await Task.Yield();
            throw new InvalidOperationException("async-probe exploded");
        }
    }

    [ActionHandler(ActionType.Ping, "*")]
    public sealed class CancelingSyncProbeHandler : IActionHandler<EmptyActionParameter, EmptyActionResult>
    {
        public Result<EmptyActionResult, ActionError> Handle(
            EmptyActionParameter param,
            WsContext ctx,
            IResolver resolver,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return this.Ok(ActionHandlerExtensions.EmptyActionResult);
        }
    }

    [ActionHandler(ActionType.GetSystemInfo, "*")]
    public sealed class CancelingAsyncProbeHandler : IAsyncActionHandler<EmptyActionParameter, EmptyActionResult>
    {
        public Task<Result<EmptyActionResult, ActionError>> HandleAsync(
            EmptyActionParameter param,
            WsContext ctx,
            IResolver resolver,
            CancellationToken ct)
        {
            return Task.FromCanceled<Result<EmptyActionResult, ActionError>>(ct);
        }
    }
}
