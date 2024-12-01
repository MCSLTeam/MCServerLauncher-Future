using MCServerLauncher.Daemon.Minecraft.Server;
using MCServerLauncher.Daemon.Minecraft.Server.Factory;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MCServerLauncher.Daemon.Remote.Action;

public static class Actions
{
    public static readonly JsonSerializer Serializer = JsonSerializer.Create(JsonSettings.Settings);

    internal static string ToSnakeCase(this string str)
    {
        return string.Concat(str.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + c : c.ToString())).ToLower();
    }
}

public class Ping
{
    public static Ping Of(JObject? data)
    {
        return new Ping();
    }

    public static JObject Response(long time)
    {
        return new JObject()
        {
            [nameof(time).ToSnakeCase()] = JToken.FromObject(time, Actions.Serializer)
        };
    }
}

public class GetJavaList
{
    public static GetJavaList Of(JObject? data)
    {
        return new GetJavaList();
    }

    public static JObject Response(List<JavaScanner.JavaInfo> javaList)
    {
        return new JObject()
        {
            [nameof(javaList).ToSnakeCase()] = JToken.FromObject(javaList, Actions.Serializer)
        };
    }
}

public class FileUploadRequest
{
    public string? Path;
    public string? Sha1;
    public long Size;
    public long? Timeout;

    public static FileUploadRequest Of(JObject? data)
    {
        return data?.ToObject<FileUploadRequest>()!;
    }

    public static JObject Response(Guid fileId)
    {
        return new JObject()
        {
            [nameof(fileId).ToSnakeCase()] = JToken.FromObject(fileId, Actions.Serializer)
        };
    }
}

public class FileUploadChunk
{
    public string Data;
    public Guid FileId;
    public long Offset;

    public static FileUploadChunk Of(JObject? data)
    {
        return data?.ToObject<FileUploadChunk>()!;
    }

    public static JObject Response(bool done, long received)
    {
        return new JObject()
        {
            [nameof(done).ToSnakeCase()] = JToken.FromObject(done, Actions.Serializer),
            [nameof(received).ToSnakeCase()] = JToken.FromObject(received, Actions.Serializer)
        };
    }
}

public class FileUploadCancel
{
    public Guid FileId;

    public static FileUploadCancel Of(JObject? data)
    {
        return data?.ToObject<FileUploadCancel>()!;
    }

    public static JObject Response()
    {
        return new JObject();
    }
}

public class FileDownloadRequest
{
    public string Path;
    public long? Timeout;

    public static FileDownloadRequest Of(JObject? data)
    {
        return data?.ToObject<FileDownloadRequest>()!;
    }

    public static JObject Response(Guid fileId, long size, string sha1)
    {
        return new JObject()
        {
            [nameof(fileId).ToSnakeCase()] = JToken.FromObject(fileId, Actions.Serializer),
            [nameof(size).ToSnakeCase()] = JToken.FromObject(size, Actions.Serializer),
            [nameof(sha1).ToSnakeCase()] = JToken.FromObject(sha1, Actions.Serializer)
        };
    }
}

public class FileDownloadRange
{
    public Guid FileId;
    public string Range;

    public static FileDownloadRange Of(JObject? data)
    {
        return data?.ToObject<FileDownloadRange>()!;
    }

    public static JObject Response(string content)
    {
        return new JObject()
        {
            [nameof(content).ToSnakeCase()] = JToken.FromObject(content, Actions.Serializer)
        };
    }
}

public class FileDownloadClose
{
    public Guid FileId;

    public static FileDownloadClose Of(JObject? data)
    {
        return data?.ToObject<FileDownloadClose>()!;
    }

    public static JObject Response()
    {
        return new JObject();
    }
}

public class GetFileInfo
{
    public string Path;

    public static GetFileInfo Of(JObject? data)
    {
        return data?.ToObject<GetFileInfo>()!;
    }

    public static JObject Response(FileMetadata meta)
    {
        return new JObject()
        {
            [nameof(meta).ToSnakeCase()] = JToken.FromObject(meta, Actions.Serializer)
        };
    }
}

public class GetDirectoryInfo
{
    public string Path;

    public static GetDirectoryInfo Of(JObject? data)
    {
        return data?.ToObject<GetDirectoryInfo>()!;
    }

    public static JObject Response(string? parent, DirectoryEntry.FileInformation[] files,
        DirectoryEntry.DirectoryInformation[] directories)
    {
        return new JObject()
        {
            [nameof(parent).ToSnakeCase()] = JToken.FromObject(parent, Actions.Serializer),
            [nameof(files).ToSnakeCase()] = JToken.FromObject(files, Actions.Serializer),
            [nameof(directories).ToSnakeCase()] = JToken.FromObject(directories, Actions.Serializer)
        };
    }
}

public class TryAddInstance
{
    public InstanceFactories Factory;
    public InstanceFactorySetting Setting;

    public static TryAddInstance Of(JObject? data)
    {
        return data?.ToObject<TryAddInstance>()!;
    }

    public static JObject Response(bool done)
    {
        return new JObject()
        {
            [nameof(done).ToSnakeCase()] = JToken.FromObject(done, Actions.Serializer)
        };
    }
}

public class TryRemoveInstance
{
    public Guid Id;

    public static TryRemoveInstance Of(JObject? data)
    {
        return data?.ToObject<TryRemoveInstance>()!;
    }

    public static JObject Response(bool done)
    {
        return new JObject()
        {
            [nameof(done).ToSnakeCase()] = JToken.FromObject(done, Actions.Serializer)
        };
    }
}

public class TryStartInstance
{
    public Guid Id;

    public static TryStartInstance Of(JObject? data)
    {
        return data?.ToObject<TryStartInstance>()!;
    }

    public static JObject Response(bool done)
    {
        return new JObject()
        {
            [nameof(done).ToSnakeCase()] = JToken.FromObject(done, Actions.Serializer)
        };
    }
}

public class TryStopInstance
{
    public Guid Id;

    public static TryStopInstance Of(JObject? data)
    {
        return data?.ToObject<TryStopInstance>()!;
    }

    public static JObject Response(bool done)
    {
        return new JObject()
        {
            [nameof(done).ToSnakeCase()] = JToken.FromObject(done, Actions.Serializer)
        };
    }
}

public class SendToInstance
{
    public Guid Id;
    public string Message;

    public static SendToInstance Of(JObject? data)
    {
        return data?.ToObject<SendToInstance>()!;
    }

    public static JObject Response()
    {
        return new JObject();
    }
}

public class KillInstance
{
    public Guid Id;

    public static KillInstance Of(JObject? data)
    {
        return data?.ToObject<KillInstance>()!;
    }

    public static JObject Response()
    {
        return new JObject();
    }
}

public class GetInstanceStatus
{
    public Guid Id;

    public static GetInstanceStatus Of(JObject? data)
    {
        return data?.ToObject<GetInstanceStatus>()!;
    }

    public static JObject Response(InstanceStatus status)
    {
        return new JObject()
        {
            [nameof(status).ToSnakeCase()] = JToken.FromObject(status, Actions.Serializer)
        };
    }
}

public class GetAllStatus
{
    public static GetAllStatus Of(JObject? data)
    {
        return new GetAllStatus();
    }

    public static JObject Response(IDictionary<Guid, InstanceStatus> status)
    {
        return new JObject()
        {
            [nameof(status).ToSnakeCase()] = JToken.FromObject(status, Actions.Serializer)
        };
    }
}