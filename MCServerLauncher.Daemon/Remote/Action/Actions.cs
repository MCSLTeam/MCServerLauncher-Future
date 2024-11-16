using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Minecraft.Server;
using MCServerLauncher.Daemon.Minecraft.Server.Factory;
using MCServerLauncher.Daemon.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MCServerLauncher.Daemon.Remote.Action;

public static class Actions
{
    public static readonly JsonSerializer Serializer = JsonSerializer.Create(JsonSettings.Settings);

    internal static string ToSnakeCase(this string str)
        => string.Concat(str.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + c : c.ToString())).ToLower();
}


public class Ping
{
    public static Ping Of(JObject? data) => new Ping();

    public static JObject Response(long time) => new JObject
    {
        [nameof(time)] = JToken.FromObject(time, Actions.Serializer)
    };
}
public class GetJavaList
{
    public static GetJavaList Of(JObject? data) => new GetJavaList();

    public static JObject Response(List<JavaScanner.JavaInfo> javaList) => new JObject
    {
        [nameof(javaList)] = JToken.FromObject(javaList, Actions.Serializer)
    };
}
public class FileUploadRequest
{
    public string? Path;
    public string? Sha1;
    public long ChunkSize;
    public long Size;

    public static FileUploadRequest Of(JObject? data) => data?.ToObject<FileUploadRequest>()!;

    public static JObject Response(Guid fileId) => new JObject
    {
        [nameof(fileId)] = JToken.FromObject(fileId, Actions.Serializer)
    };
}
public class FileUploadChunk
{
    public Guid FileId;
    public long Offset;
    public string Data;

    public static FileUploadChunk Of(JObject? data) => data?.ToObject<FileUploadChunk>()!;

    public static JObject Response(bool done, long received) => new JObject
    {
        [nameof(done)] = JToken.FromObject(done, Actions.Serializer),
        [nameof(received)] = JToken.FromObject(received, Actions.Serializer)
    };
}
public class FileUploadCancel
{
    public Guid FileId;

    public static FileUploadCancel Of(JObject? data) => data?.ToObject<FileUploadCancel>()!;

    public static JObject Response() => new JObject
    {

    };
}
public class FileDownloadRequest
{
    public string Path;

    public static FileDownloadRequest Of(JObject? data) => data?.ToObject<FileDownloadRequest>()!;

    public static JObject Response(Guid fileId, long size, string sha1) => new JObject
    {
        [nameof(fileId)] = JToken.FromObject(fileId, Actions.Serializer),
        [nameof(size)] = JToken.FromObject(size, Actions.Serializer),
        [nameof(sha1)] = JToken.FromObject(sha1, Actions.Serializer)
    };
}
public class FileDownloadRange
{
    public Guid FileId;
    public string Range;

    public static FileDownloadRange Of(JObject? data) => data?.ToObject<FileDownloadRange>()!;

    public static JObject Response(string content) => new JObject
    {
        [nameof(content)] = JToken.FromObject(content, Actions.Serializer)
    };
}
public class FileDownloadClose
{
    public Guid FileId;

    public static FileDownloadClose Of(JObject? data) => data?.ToObject<FileDownloadClose>()!;

    public static JObject Response() => new JObject
    {

    };
}
public class GetFileInfo
{
    public string Path;

    public static GetFileInfo Of(JObject? data) => data?.ToObject<GetFileInfo>()!;

    public static JObject Response(FileMetadata meta) => new JObject
    {
        [nameof(meta)] = JToken.FromObject(meta, Actions.Serializer)
    };
}
public class GetDirectoryInfo
{
    public string Path;

    public static GetDirectoryInfo Of(JObject? data) => data?.ToObject<GetDirectoryInfo>()!;

    public static JObject Response(string? parent, DirectoryEntry.FileInformation[] files, DirectoryEntry.DirectoryInformation[] directories) => new JObject
    {
        [nameof(parent)] = JToken.FromObject(parent, Actions.Serializer),
        [nameof(files)] = JToken.FromObject(files, Actions.Serializer),
        [nameof(directories)] = JToken.FromObject(directories, Actions.Serializer)
    };
}
public class TryAddInstance
{
    public InstanceFactorySetting Setting;
    public InstanceFactories Factory;

    public static TryAddInstance Of(JObject? data) => data?.ToObject<TryAddInstance>()!;

    public static JObject Response(bool done) => new JObject
    {
        [nameof(done)] = JToken.FromObject(done, Actions.Serializer)
    };
}
public class TryRemoveInstance
{
    public Guid Id;

    public static TryRemoveInstance Of(JObject? data) => data?.ToObject<TryRemoveInstance>()!;

    public static JObject Response(bool done) => new JObject
    {
        [nameof(done)] = JToken.FromObject(done, Actions.Serializer)
    };
}
public class TryStartInstance
{
    public Guid Id;

    public static TryStartInstance Of(JObject? data) => data?.ToObject<TryStartInstance>()!;

    public static JObject Response(bool done) => new JObject
    {
        [nameof(done)] = JToken.FromObject(done, Actions.Serializer)
    };
}
public class TryStopInstance
{
    public Guid Id;

    public static TryStopInstance Of(JObject? data) => data?.ToObject<TryStopInstance>()!;

    public static JObject Response(bool done) => new JObject
    {
        [nameof(done)] = JToken.FromObject(done, Actions.Serializer)
    };
}
public class SendToInstance
{
    public Guid Id;
    public string Message;

    public static SendToInstance Of(JObject? data) => data?.ToObject<SendToInstance>()!;

    public static JObject Response() => new JObject
    {

    };
}
public class KillInstance
{
    public Guid Id;

    public static KillInstance Of(JObject? data) => data?.ToObject<KillInstance>()!;

    public static JObject Response() => new JObject
    {

    };
}
public class GetInstanceStatus
{
    public Guid Id;

    public static GetInstanceStatus Of(JObject? data) => data?.ToObject<GetInstanceStatus>()!;

    public static JObject Response(InstanceStatus status) => new JObject
    {
        [nameof(status)] = JToken.FromObject(status, Actions.Serializer)
    };
}
public class GetAllStatus
{
    public static GetAllStatus Of(JObject? data) => new GetAllStatus();

    public static JObject Response(IDictionary<Guid, InstanceStatus> status) => new JObject
    {
        [nameof(status)] = JToken.FromObject(status, Actions.Serializer)
    };
}
