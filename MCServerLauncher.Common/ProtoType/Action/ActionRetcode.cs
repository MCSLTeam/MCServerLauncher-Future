using System.Reflection;

namespace MCServerLauncher.Common.ProtoType.Action;

public class ActionRetcode
{
    private static readonly Dictionary<int, ActionRetcode> RetcodeMap = new();

    private ActionRetcode(int code, string message)
    {
        Code = code;
        Message = message;
    }

    public int Code { get; }
    public string Message { get; }

    public static ActionRetcode FromCode(int code)
    {
        if (RetcodeMap.TryGetValue(code, out var value))
        {
            return value;
        }

        throw new ArgumentException($"Failed to get retcode: {code}");
    }

    public ActionRetcode WithMessage(object? message)
    {
        if (message == null) return this;
        var msg = message.ToString();
        return new ActionRetcode(Code, msg == Message ? msg : Message + ": " + msg);
    }

    public ActionRetcode WithException(Exception? e = null)
    {
        if (e == null) return this;
        var msg = e.Message == Message ? e.Message : Message + ": " + e.Message;
        return WithMessage(msg);
    }

    public override string ToString()
    {
        return $"ActionRetcode: {{ Code = {Code}, Message = {Message} }}";
    }

    #region ActionRetcodes

    public static readonly ActionRetcode Ok;

    #region Request Error

    public static readonly ActionRetcode RequestError;

    public static readonly ActionRetcode BadRequest;

    public static readonly ActionRetcode UnknownAction;

    public static readonly ActionRetcode PermissionDenied;

    public static readonly ActionRetcode ActionUnavailable;

    public static readonly ActionRetcode RateLimitExceeded;

    public static readonly ActionRetcode ParamError;

    #endregion

    #region ServerError

    public static readonly ActionRetcode UnexpectedError;

    #endregion

    #region FileError

    public static readonly ActionRetcode FileError;

    public static readonly ActionRetcode FileNotFound;

    public static readonly ActionRetcode FileAlreadyExists;

    public static readonly ActionRetcode FileInUse;

    public static readonly ActionRetcode ItsADirectory;

    public static readonly ActionRetcode ItsAFile;

    public static readonly ActionRetcode FileAccessDenied;

    public static readonly ActionRetcode DiskFull;

    #endregion

    #region Upload / Download Error

    public static readonly ActionRetcode UploadDownloadError;

    public static readonly ActionRetcode AlreadyUploadingDownloading;

    public static readonly ActionRetcode NotUploadingDownloading;

    public static readonly ActionRetcode FileTooBig;

    #endregion

    #region Instance Error

    public static readonly ActionRetcode InstanceError;

    public static readonly ActionRetcode InstanceNotFound;

    public static readonly ActionRetcode InstanceAlreadyExists;

    public static readonly ActionRetcode BadInstanceState;

    public static readonly ActionRetcode BadInstanceType;

    #region Instance Action Error

    public static readonly ActionRetcode InstanceActionError;

    public static readonly ActionRetcode InstallationError;

    public static readonly ActionRetcode ProcessError;

    #endregion

    #endregion

    #endregion

    static ActionRetcode()
    {
        Ok = Register(0, "OK");

        RequestError = Register(10000, "Request Error");
        BadRequest = Register(10001, "Bad Request");
        UnknownAction = Register(10002, "Unknown Action");
        PermissionDenied = Register(10003, "Permission Denied");
        ActionUnavailable = Register(10004, "Action Unavailable");
        RateLimitExceeded = Register(10005, "Rate Limit Exceeded");
        ParamError = Register(10006, "Param Error");

        UnexpectedError = Register(20001, "Unexpected Error");

        FileError = Register(21000, "File Error");
        FileNotFound = Register(21001, "File Not Found");
        FileAlreadyExists = Register(21002, "File Already Exists");
        FileInUse = Register(21003, "File In Use");
        ItsADirectory = Register(21004, "It's A Directory");
        ItsAFile = Register(21005, "It's A File");
        FileAccessDenied = Register(21006, "File Access Denied");
        DiskFull = Register(21007, "Disk Full");

        UploadDownloadError = Register(21100, "Upload/Download Error");
        AlreadyUploadingDownloading = Register(21101, "Already Uploading/Downloading");
        NotUploadingDownloading = Register(21102, "Not Uploading/Downloading");
        FileTooBig = Register(21103, "File Too Big");

        InstanceError = Register(30000, "Instance Error");
        InstanceNotFound = Register(30001, "Instance Not Found");
        InstanceAlreadyExists = Register(30002, "Instance Already Exists");
        BadInstanceState = Register(30003, "Bad Instance State");
        BadInstanceType = Register(30004, "Bad Instance Type");

        InstanceActionError = Register(31001, "Instance Action Error");
        InstallationError = Register(31002, "Installation Error");
        ProcessError = Register(31003, "Process Error");
    }

    private static ActionRetcode Register(int code, string message)
    {
        var actionRetcode = new ActionRetcode(code, message);
        RetcodeMap[code] = actionRetcode;
        return actionRetcode;
    }
}