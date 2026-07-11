using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MCServerLauncher.Common.Network;

// https://c4k3.github.io/wiki.vg/Server_List_Ping.html
public class SlpClient
{
    private List<byte> Buffer { get; } = new();
    private TcpClient Client { get; } = new();

    /// <summary>
    ///     获取 SLP 的 status 和 ping 信息,用于1.7以上
    /// </summary>
    /// <param name="host"></param>
    /// <param name="port"></param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="ArgumentOutOfRangeException">port</exception>
    /// <returns></returns>
    public static async Task<SlpStatus?> GetStatusModern(string host, int port,
        CancellationToken cancellationToken = default)
    {
        if (port < 0 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));

        try
        {
            var client = new SlpClient();
            if (!await client.HandShakeAsync(host, (ushort)port, cancellationToken)) return null;

            var payload = await client.GetSlpAsync(cancellationToken);
            if (payload is null) return null;

            var latency = await client.GetLatencyAsync(cancellationToken);

            return latency != null ? new SlpStatus(payload, latency.Value) : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e) when (e is SocketException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    ///     获取 SLP 的 status,用于 1.7 以下
    /// </summary>
    /// <param name="host"></param>
    /// <param name="port"></param>
    /// <param name="cancellationToken"></param>
    /// ///
    /// <exception cref="ArgumentOutOfRangeException">port</exception>
    /// <returns></returns>
    public static async Task<SlpLegacyStatus?> GetStatusLegacy(string host, int port,
        CancellationToken cancellationToken = default)
    {
        if (port < 0 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));

        using var client = new TcpClient();

        // connect to server
        await client.ConnectAsync(host, port);
        if (!client.Connected) return null;

        using var stream = client.GetStream();

        // send request
        await stream.WriteAsync(new byte[] { 0xFE, 0x01 }, 0, 2, cancellationToken);

        // read response
        var buffer = new byte[2048];
        var length = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);

        // validate response
        if (buffer[0] != 0xFF)
        {
            return null;
        }

        var payload = Encoding.BigEndianUnicode.GetString(buffer, 3, length - 3);
        if (payload.StartsWith("§"))
        {
            var data = payload.Split('\u0000');

            var pingVersion = int.Parse(data[0].Substring(1));
            var protocolVersion = int.Parse(data[1]);
            var gameVersion = data[2];
            var motd = data[3];
            var playersOnline = int.Parse(data[4]);
            var maxPlayers = int.Parse(data[5]);
            return new SlpLegacyStatus(motd, playersOnline, maxPlayers)
            {
                ProtocolVersion = protocolVersion,
                GameVersion = gameVersion,
                PingVersion = pingVersion
            };
        }
        else
        {
            var data = payload.Split('\u0000');

            var motd = data[0];
            var playersOnline = int.Parse(data[1]);
            var maxPlayers = int.Parse(data[2]);
            return new SlpLegacyStatus(motd, playersOnline, maxPlayers);
        }
    }

    private async Task FlushAsync(int id = -1, CancellationToken cancellationToken = default)
    {
        var data = Buffer.ToArray();
        Buffer.Clear();

        var add = 0;
        var packetData = new[] { (byte)0x00 };
        if (id >= 0)
        {
            WriteVarInt(id);
            packetData = Buffer.ToArray();
            add = packetData.Length;
            Buffer.Clear();
        }

        WriteVarInt(data.Length + add);
        var bufferLength = Buffer.ToArray();
        Buffer.Clear();

        var stream = Client.GetStream();

        await stream.WriteAsync(bufferLength, 0, bufferLength.Length, cancellationToken);
        await stream.WriteAsync(packetData, 0, packetData.Length, cancellationToken);
        await stream.WriteAsync(data, 0, data.Length, cancellationToken);
    }

    public async Task<bool> HandShakeAsync(string host, ushort port, CancellationToken cancellationToken = default)
    {
        // create connection
        await Client.ConnectAsync(host, port);
        if (!Client.Connected)
        {
            return false;
        }

        // send handshake
        WriteVarInt(47); // 1. protocol version (Varint)
        WriteString(host); // 2. server hostname (String)
        WriteShort(port); // 3. server port (Unsigned Short)
        WriteVarInt(1); // 4. next state (Varint)

        await FlushAsync(0, cancellationToken);
        return true;
    }

    public async Task<PingPayload?> GetSlpAsync(CancellationToken cancellationToken = default)
    {
        // status : protocol 0x00
        // send status request
        await FlushAsync(0, cancellationToken);

        // process response
        var received = new byte[0x10000];
        var offset = 0;

        // dispose return value
        var _ = await Client.GetStream().ReadAsync(received, 0, received.Length, cancellationToken);

        try
        {
            ReadVarInt(received, ref offset);
            ReadVarInt(received, ref offset);
            var jsonLength = ReadVarInt(received, ref offset);

            var json = ReadString(received, jsonLength, ref offset);
            return JsonSerializer.Deserialize(json, SlpJsonContext.Default.PingPayload);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<TimeSpan?> GetLatencyAsync(CancellationToken cancellationToken = default)
    {
        // ping : protocol 0x01
        var sendTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        WriteLong(sendTime);
        await FlushAsync(1, cancellationToken);

        var received = new byte[16];
        var _ = await Client.GetStream().ReadAsync(received, 0, received.Length, cancellationToken);
        var receivedTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        try
        {
            var offset = 0;
            ReadVarInt(received, ref offset);
            ReadVarInt(received, ref offset);

            ReadLong(received, ref offset);
            return TimeSpan.FromMilliseconds(receivedTime - sendTime);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void WriteShort(ushort value)
    {
        Buffer.AddRange(BitConverter.GetBytes(value));
    }

    private void WriteVarInt(int value)
    {
        while (value >= 0x80)
        {
            Buffer.Add((byte)(value | 0x80)); // 设置最高位，表示后续还有字节
            value >>= 7; // 右移 7 位
        }

        Buffer.Add((byte)value); // 最后一个字节不设置最高位
    }

    private void WriteLong(long value)
    {
        Buffer.AddRange(BitConverter.GetBytes(value));
    }

    private void WriteString(string value)
    {
        var data = Encoding.UTF8.GetBytes(value);
        WriteVarInt(data.Length);
        Buffer.AddRange(data);
    }

    private static int ReadVarInt(byte[] data, ref int offset)
    {
        var result = 0;
        var shift = 0;
        while (true)
        {
            var b = data[offset++];
            result |= (b & 0x7F) << shift;
            if ((b & 0x80) != 0) shift += 7;
            else return result;
        }
    }

    private static long ReadLong(byte[] data, ref int offset)
    {
        var rv = BitConverter.ToInt64(data, offset);
        offset += 8;
        return rv;
    }

    private static string ReadString(byte[] data, int length, ref int offset)
    {
        var str = Encoding.UTF8.GetString(data, offset, length);
        offset += length;
        return str;
    }
}

#region Motd Utils

internal static class MotdUtils
{
    public static IDictionary<char, string> MinecraftStyles { get; } = new Dictionary<char, string>
    {
        { 'k', "none;font-weight:normal;font-style:normal" },
        { 'm', "line-through;font-weight:normal;font-style:normal" },
        { 'l', "none;font-weight:900;font-style:normal" },
        { 'n', "underline;font-weight:normal;font-style:normal;" },
        { 'o', "none;font-weight:normal;font-style:italic;" },
        { 'r', "none;font-weight:normal;font-style:normal;color:#FFFFFF;" }
    };

    private static IDictionary<char, string> MinecraftColors { get; } = new Dictionary<char, string>
    {
        { '0', "#000000" }, { '1', "#0000AA" }, { '2', "#00AA00" }, { '3', "#00AAAA" }, { '4', "#AA0000" },
        { '5', "#AA00AA" }, { '6', "#FFAA00" }, { '7', "#AAAAAA" }, { '8', "#555555" }, { '9', "#5555FF" },
        { 'a', "#55FF55" }, { 'b', "#55FFFF" }, { 'c', "#FF5555" }, { 'd', "#FF55FF" }, { 'e', "#FFFF55" },
        { 'f', "#FFFFFF" }
    };

    public static string MotdHtml(string motd)
    {
        var regex = new Regex("§([k-oK-O])(.*?)(§[0-9a-fA-Fk-oK-OrR]|$)");
        while (regex.IsMatch(motd))
            motd = regex.Replace(motd, m =>
            {
                var ast = "text-decoration:" + MinecraftStyles[m.Groups[1].Value[0]];
                var html = "<span style=\"" + ast + "\">" + m.Groups[2].Value + "</span>" + m.Groups[3].Value;
                return html;
            });
        regex = new Regex("§([0-9a-fA-F])(.*?)(§[0-9a-fA-FrR]|$)");
        while (regex.IsMatch(motd))
            motd = regex.Replace(motd, m =>
            {
                var ast = "color:" + MinecraftColors[m.Groups[1].Value[0]];
                var html = "<span style=\"" + ast + "\">" + m.Groups[2].Value + "</span>" + m.Groups[3].Value;
                return html;
            });
        return motd;
    }
}

#endregion

#region Server ping

public record SlpStatus(PingPayload Payload, TimeSpan Latency);

public record SlpLegacyStatus
{
    public SlpLegacyStatus(string motd, int playersOnline, int maxPlayers)
    {
        Motd = motd;
        PlayersOnline = playersOnline;
        MaxPlayers = maxPlayers;
    }

    public int PingVersion { get; set; }
    public int ProtocolVersion { get; set; }
    public string GameVersion { get; set; } = string.Empty;

    public string Motd { get; set; }
    public int PlayersOnline { get; set; }
    public int MaxPlayers { get; set; }
}

/// <summary>
///     C# represenation of the following JSON file
///     https://gist.github.com/thinkofdeath/6927216
/// </summary>
public record PingPayload
{
    /// <summary>
    ///     Protocol that the server is using and the given name
    /// </summary>
    [JsonPropertyName("version")]
    public VersionPayload Version { get; set; } = null!;

    [JsonPropertyName("players")]
    public PlayersPayload Players { get; set; } = null!;

    [JsonPropertyName("description")]
    [JsonConverter(typeof(DescriptionStjConverter))]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    ///     Server icon, important to note that it's encoded in base64
    /// </summary>
    [JsonPropertyName("favicon")]
    public string Icon { get; set; } = string.Empty;
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(PingPayload))]
public partial class SlpJsonContext : JsonSerializerContext
{
}

/// <summary>
///     STJ converter for Minecraft description field (can be string or {"text":"..."} object).
/// </summary>
public class DescriptionStjConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return reader.GetString();

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            return doc.RootElement.TryGetProperty("text", out var text) ? text.GetString() : null;
        }

        reader.Skip();
        return null;
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }
}

public record VersionPayload
{
    [JsonPropertyName("protocol")]
    public int Protocol { get; set; }

    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
}

public record PlayersPayload
{
    [JsonPropertyName("max")] public int Max { get; set; }

    [JsonPropertyName("online")]
    public int Online { get; set; }

    [JsonPropertyName("sample")]
    public PlayerSample[] Sample { get; set; } = Array.Empty<PlayerSample>();
}

public record PlayerSample
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;

    [JsonPropertyName("id")] public Guid Id { get; set; }
}

#endregion
