using System.Reflection;
using Newtonsoft.Json;

namespace MCServerLauncher.Common.ProtoType.Action;

public class ActionRetcode
{
    public int Code { get; }
    public string Message { get; private set; }

    internal ActionRetcode(int code, string message)
    {
        Code = code;
        Message = message;
    }

    public static ActionRetcode FromCode(int code)
    {
        var type = typeof(ActionRetcode);
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Static);
        foreach (var property in properties)
        {
            if (property.GetValue(null) is ActionRetcode actionRetcode)
            {
                if (actionRetcode.Code == code) return actionRetcode;
            }
        }

        throw new ArgumentException($"Failed to get retcode: {code}");
    }

    public ActionRetcode WithMessage(object? message)
    {
        if (message == null) return this;
        var msg = message.ToString();
        Message = msg == Message ? msg : Message + ": " + msg;
        return this;
    }

    public ActionRetcode WithException(Exception? e = null)
    {
        if (e == null) return this;
        var msg = e.Message == Message ? e.Message : Message + ": " + e.Message;
        return WithMessage(msg);
    }

    #region ActionRetcodes

    public static ActionRetcode Ok = new(0, "OK");

    #region Request Error

    public static ActionRetcode RequestError = new(10000, "Request Error");

    public static ActionRetcode BadRequest = new(10001, "Bad Request");

    public static ActionRetcode UnknownAction = new(10002, "Unknown Action");

    public static ActionRetcode PermissionDenied = new(10003, "Permission Denied");

    public static ActionRetcode ActionUnavailable = new(10004, "Action Unavailable");

    public static ActionRetcode RateLimitExceeded = new(10005, "Rate Limit Exceeded");

    public static ActionRetcode ParamError = new(10006, "Param Error");

    #endregion

    #region ServerError

    public static ActionRetcode UnexpectedError = new(20001, "Unexpected Error");

    #endregion

    #region FileError

    public static ActionRetcode FileError = new(20000, "File Error");

    public static ActionRetcode FileNotFound = new(21001, "File Not Found");

    public static ActionRetcode FileAlreadyExists = new(21002, "File Already Exists");

    public static ActionRetcode FileInUse = new(21003, "File In Use");

    public static ActionRetcode ItsADirectory = new(21004, "It's A Directory");

    public static ActionRetcode ItsAFile = new(21005, "It's A File");

    public static ActionRetcode FileAccessDenied = new(21006, "File Access Denied");

    public static ActionRetcode DiskFull = new(21007, "Disk Full");

    #endregion

    #region Upload / Download Error

    public static ActionRetcode UploadError = new(21100, "Upload Error");

    public static ActionRetcode DownloadError = new(21100, "Download Error");

    public static ActionRetcode AlreadyUploading = new(21101, "Already Uploading");

    public static ActionRetcode AlreadyDownloading = new(21101, "Already Downloading");

    public static ActionRetcode NotUploading = new(21102, "Not Uploading");

    public static ActionRetcode NotDownloading = new(21102, "Not Downloading");

    public static ActionRetcode FileTooBig = new(21103, "File Too Big");

    #endregion

    #region Instance Error

    public static ActionRetcode InstanceError = new(30000, "Instance Error");

    public static ActionRetcode InstanceNotFound = new(30001, "Instance Not Found");

    public static ActionRetcode InstanceAlreadyExists = new(30002, "Instance Already Exists");

    public static ActionRetcode BadInstanceState = new(30003, "Bad Instance State");

    public static ActionRetcode BadInstanceType = new(30004, "Bad Instance Type");

    #region Instance Action Error

    public static ActionRetcode InstanceActionError = new(31001, "Instance Action Error");

    public static ActionRetcode InstallationError = new(31002, "Installation Error");

    public static ActionRetcode ProcessError = new(31003, "Process Error");

    #endregion

    #endregion

    #endregion
}