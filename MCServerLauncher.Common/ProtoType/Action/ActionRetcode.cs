using System.Reflection;

namespace MCServerLauncher.Common.ProtoType.Action;

public class ActionRetcode
{
    private static readonly Dictionary<int, ActionRetcode> RetcodeMap = new();

    static ActionRetcode()
    {
    }

    private ActionRetcode(int code, string message)
    {
        Code = code;
        Message = message;
    }

    public int Code { get; }
    public string Message { get; }

    public static ActionRetcode FromCode(int code)
    {
        var type = typeof(ActionRetcode);
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Static);
        foreach (var property in properties)
            if (property.GetValue(null) is ActionRetcode actionRetcode)
                if (actionRetcode.Code == code)
                    return actionRetcode;

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

    #region ActionRetcodes

    public static readonly ActionRetcode Ok = new(0, "OK");

    #region Request Error

    public static readonly ActionRetcode RequestError = new(10000, "Request Error");

    public static readonly ActionRetcode BadRequest = new(10001, "Bad Request");

    public static readonly ActionRetcode UnknownAction = new(10002, "Unknown Action");

    public static readonly ActionRetcode PermissionDenied = new(10003, "Permission Denied");

    public static readonly ActionRetcode ActionUnavailable = new(10004, "Action Unavailable");

    public static readonly ActionRetcode RateLimitExceeded = new(10005, "Rate Limit Exceeded");

    public static readonly ActionRetcode ParamError = new(10006, "Param Error");

    #endregion

    #region ServerError

    public static readonly ActionRetcode UnexpectedError = new(20001, "Unexpected Error");

    #endregion

    #region FileError

    public static readonly ActionRetcode FileError = new(20000, "File Error");

    public static readonly ActionRetcode FileNotFound = new(21001, "File Not Found");

    public static readonly ActionRetcode FileAlreadyExists = new(21002, "File Already Exists");

    public static readonly ActionRetcode FileInUse = new(21003, "File In Use");

    public static readonly ActionRetcode ItsADirectory = new(21004, "It's A Directory");

    public static readonly ActionRetcode ItsAFile = new(21005, "It's A File");

    public static readonly ActionRetcode FileAccessDenied = new(21006, "File Access Denied");

    public static readonly ActionRetcode DiskFull = new(21007, "Disk Full");

    #endregion

    #region Upload / Download Error

    public static readonly ActionRetcode UploadError = new(21100, "Upload Error");

    public static readonly ActionRetcode DownloadError = new(21100, "Download Error");

    public static readonly ActionRetcode AlreadyUploading = new(21101, "Already Uploading");

    public static readonly ActionRetcode AlreadyDownloading = new(21101, "Already Downloading");

    public static readonly ActionRetcode NotUploading = new(21102, "Not Uploading");

    public static readonly ActionRetcode NotDownloading = new(21102, "Not Downloading");

    public static readonly ActionRetcode FileTooBig = new(21103, "File Too Big");

    #endregion

    #region Instance Error

    public static readonly ActionRetcode InstanceError = new(30000, "Instance Error");

    public static readonly ActionRetcode InstanceNotFound = new(30001, "Instance Not Found");

    public static readonly ActionRetcode InstanceAlreadyExists = new(30002, "Instance Already Exists");

    public static readonly ActionRetcode BadInstanceState = new(30003, "Bad Instance State");

    public static readonly ActionRetcode BadInstanceType = new(30004, "Bad Instance Type");

    #region Instance Action Error

    public static readonly ActionRetcode InstanceActionError = new(31001, "Instance Action Error");

    public static readonly ActionRetcode InstallationError = new(31002, "Installation Error");

    public static readonly ActionRetcode ProcessError = new(31003, "Process Error");

    #endregion

    #endregion

    #endregion
}