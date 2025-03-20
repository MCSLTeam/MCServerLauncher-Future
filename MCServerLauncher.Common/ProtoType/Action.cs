namespace MCServerLauncher.Common.ProtoType.Action
{
    public enum ActionType
    {
        // Event subsystem
        SubscribeEvent,
        UnsubscribeEvent,

        // MISC
        Ping,
        GetSystemInfo,
        GetPermissions,
        GetJavaList,
        GetDirectoryInfo,
        GetFileInfo,

        // File down/upload
        FileUploadRequest,
        FileUploadChunk,
        FileUploadCancel,
        FileDownloadRequest,
        FileDownloadChunk,
        FileDownloadClose,

        // Instance operation
        AddInstance,
        RemoveInstance,
        StartInstance,
        StopInstance,
        KillInstance,
        SendToInstance,
        GetInstanceStatus
    }
}

namespace MCServerLauncher.Common.ProtoType.Action.Parameters
{
    public interface IActionParameter
    {
    }

    public record EmptyActionParameter;
}

namespace MCServerLauncher.Common.ProtoType.Action.Results
{
    public interface IActionResult
    {
    }

    public record EmptyActionResult;
}