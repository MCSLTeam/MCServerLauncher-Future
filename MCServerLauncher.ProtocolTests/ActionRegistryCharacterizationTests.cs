using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Status;
using MCServerLauncher.Daemon.Remote.Action;

namespace MCServerLauncher.ProtocolTests;

[Collection("LegacyActionRegistryIsolation")]
public class ActionRegistryCharacterizationTests
{
    private static readonly Guid PingRequestId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid GetSystemInfoRequestId = Guid.Parse("88888888-8888-8888-8888-888888888888");

    private static readonly ActionType[] ExpectedInventory =
    [
        ActionType.SubscribeEvent,
        ActionType.UnsubscribeEvent,
        ActionType.Ping,
        ActionType.GetSystemInfo,
        ActionType.GetPermissions,
        ActionType.GetJavaList,
        ActionType.GetDirectoryInfo,
        ActionType.GetFileInfo,
        ActionType.FileUploadRequest,
        ActionType.FileUploadChunk,
        ActionType.FileUploadCancel,
        ActionType.FileDownloadRequest,
        ActionType.FileDownloadRange,
        ActionType.FileDownloadClose,
        ActionType.DeleteFile,
        ActionType.DeleteDirectory,
        ActionType.RenameFile,
        ActionType.RenameDirectory,
        ActionType.CreateDirectory,
        ActionType.MoveFile,
        ActionType.MoveDirectory,
        ActionType.CopyFile,
        ActionType.CopyDirectory,
        ActionType.AddInstance,
        ActionType.RemoveInstance,
        ActionType.StartInstance,
        ActionType.StopInstance,
        ActionType.KillInstance,
        ActionType.SendToInstance,
        ActionType.GetInstanceReport,
        ActionType.GetAllReports,
        ActionType.GetInstanceLogHistory,
        ActionType.GetEventRules,
        ActionType.SaveEventRules
    ];

    private static readonly ActionType[] ExpectedAsyncActions =
    [
        ActionType.GetSystemInfo,
        ActionType.GetJavaList,
        ActionType.FileUploadChunk,
        ActionType.FileDownloadRequest,
        ActionType.FileDownloadRange,
        ActionType.AddInstance,
        ActionType.StartInstance,
        ActionType.GetInstanceReport,
        ActionType.GetAllReports
    ];

    [Fact]
    [Trait("Category", "LegacyActionRegistry")]
    public void LegacyActionInventory_RegistersCurrentBaselineActionSet()
    {
        var snapshot = LegacyActionRegistryHarness.BuildProductionSnapshot();

        Assert.Equal(ExpectedInventory.Length, snapshot.HandlerCount);
        Assert.Equal(Order(ExpectedInventory), Order(snapshot.HandlerMetas.Keys));
    }

    [Fact]
    [Trait("Category", "LegacyActionRegistry")]
    public void LegacyActionClassification_CurrentSyncAndAsyncPartitionMatchesBaseline()
    {
        var snapshot = LegacyActionRegistryHarness.BuildProductionSnapshot();
        var expectedSyncActions = Order(ExpectedInventory.Except(ExpectedAsyncActions));
        var expectedAsyncActions = Order(ExpectedAsyncActions);

        Assert.Equal(expectedSyncActions, Order(snapshot.SyncHandlers.Keys));
        Assert.Equal(expectedAsyncActions, Order(snapshot.AsyncHandlers.Keys));
        Assert.Empty(snapshot.SyncHandlers.Keys.Intersect(snapshot.AsyncHandlers.Keys));

        Assert.All(expectedSyncActions, action => Assert.Equal(EActionHandlerType.Sync, snapshot.HandlerMetas[action].Type));
        Assert.All(expectedAsyncActions, action => Assert.Equal(EActionHandlerType.Async, snapshot.HandlerMetas[action].Type));
    }

    [Fact]
    [Trait("Category", "LegacyActionRegistry")]
    public void LegacyPingDispatch_EmptyParams_ReturnsOkEnvelopeWithTimestampPayload()
    {
        var snapshot = LegacyActionRegistryHarness.BuildProductionSnapshot();
        var resolver = LegacyActionRegistryHarness.CreateResolver();
        var context = LegacyActionRegistryHarness.CreateContext();
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var response = snapshot.SyncHandlers[ActionType.Ping].Invoke(
            LegacyActionRegistryHarness.ParseElement("{}"),
            PingRequestId,
            context,
            resolver,
            CancellationToken.None);

        var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var payload = LegacyActionRegistryHarness.DeserializeData<PingResult>(response);

        Assert.Equal(ActionRequestStatus.Ok, response.RequestStatus);
        Assert.Equal(ActionRetcode.Ok.Code, response.Retcode);
        Assert.Equal(ActionRetcode.Ok.Message, response.Message);
        Assert.Equal(PingRequestId, response.Id);
        Assert.InRange(payload.Time, before, after);
    }

    [Fact]
    [Trait("Category", "LegacyActionRegistry")]
    public async Task LegacyGetSystemInfoDispatch_EmptyParams_ReturnsOkEnvelopeWithTypedPayload()
    {
        var snapshot = LegacyActionRegistryHarness.BuildProductionSnapshot();
        var expectedSystemInfo = LegacyActionRegistryHarness.CreateSystemInfo();
        var resolver = LegacyActionRegistryHarness.CreateResolver(expectedSystemInfo);
        var context = LegacyActionRegistryHarness.CreateContext();

        var response = await snapshot.AsyncHandlers[ActionType.GetSystemInfo].Invoke(
            LegacyActionRegistryHarness.ParseElement("{}"),
            GetSystemInfoRequestId,
            context,
            resolver,
            CancellationToken.None);

        var payload = LegacyActionRegistryHarness.DeserializeData<GetSystemInfoResult>(response);

        Assert.Equal(ActionRequestStatus.Ok, response.RequestStatus);
        Assert.Equal(ActionRetcode.Ok.Code, response.Retcode);
        Assert.Equal(ActionRetcode.Ok.Message, response.Message);
        Assert.Equal(GetSystemInfoRequestId, response.Id);
        Assert.Equal(expectedSystemInfo.Os.Name, payload.Info.Os.Name);
        Assert.Equal(expectedSystemInfo.Os.Arch, payload.Info.Os.Arch);
        Assert.Equal(expectedSystemInfo.Cpu.Vendor, payload.Info.Cpu.Vendor);
        Assert.Equal(expectedSystemInfo.Cpu.Name, payload.Info.Cpu.Name);
        Assert.Equal(expectedSystemInfo.Cpu.Count, payload.Info.Cpu.Count);
        Assert.Equal(expectedSystemInfo.Mem.Total, payload.Info.Mem.Total);
        Assert.Equal(expectedSystemInfo.Mem.Free, payload.Info.Mem.Free);
        Assert.Equal(expectedSystemInfo.Drive.DriveFormat, payload.Info.Drive.DriveFormat);
        Assert.Equal(expectedSystemInfo.Drive.Total, payload.Info.Drive.Total);
        Assert.Equal(expectedSystemInfo.Drive.Free, payload.Info.Drive.Free);
    }

    private static ActionType[] Order(IEnumerable<ActionType> actions)
    {
        return actions.OrderBy(action => (int)action).ToArray();
    }
}
