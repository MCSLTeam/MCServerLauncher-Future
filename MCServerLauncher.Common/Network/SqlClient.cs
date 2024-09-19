using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Serilog;

namespace MCServerLauncher.Common.Network;

public class SlpClient
{
    private List<byte> Buffer { get; } = new();
    private TcpClient Client { get; } = new();

    /// <summary>
    ///  获取SLP的status和ping信息,用于1.7以上
    /// </summary>
    /// <param name="host"></param>
    /// <param name="port"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public static async Task<SlpStatus?> GetStatusModern(string host, ushort port,
        CancellationToken cancellationToken = default)
    {
        var client = new SlpClient();
        if (!await client.HandShakeAsync(host, port, cancellationToken)) return null;

        var payload = await client.GetSlpAsync(cancellationToken);
        var latency = await client.GetLatencyAsync(cancellationToken);

        if (payload != null && latency != null) return new SlpStatus(payload, latency.Value);
        return null;
    }

    /// <summary>
    /// 获取SLP的status,用于1.7以下
    /// </summary>
    /// <param name="host"></param>
    /// <param name="port"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public static async Task<SlpLegacyStatus?> GetStatusLegacy(string host, ushort port,
        CancellationToken cancellationToken = default)
    {
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
            Log.Error("[SlpClient] Received invalid packet");
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
            Log.Error("[SLP-Client] Cant connect to {0}:{1}", host, port);
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
            var length = ReadVarInt(received, ref offset);
            var packetId = ReadVarInt(received, ref offset);
            var jsonLength = ReadVarInt(received, ref offset);
            Log.Debug("[SlpClient]Received packetId 0x{0:X2} with a length of {1}", packetId, length);

            var json = ReadString(received, jsonLength, ref offset);
            return JsonConvert.DeserializeObject<PingPayload>(json);
        }
        catch (Exception e)
        {
            Log.Error("[SlpClient] Failed to parse server ping payload: {0}", e);
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
            var length = ReadVarInt(received, ref offset);
            var packetId = ReadVarInt(received, ref offset);
            Log.Debug("[SlpClient]Received packetId 0x{0:X2} with a length of {1}", packetId, length);

            // validate pong packet
            var echo = ReadLong(received, ref offset);
            if (echo != sendTime)
            {
                Log.Warning(
                    "[SlpClient] Received echo time is not equal to send time, are we connected to a official mc server?");
            }

            return TimeSpan.FromMilliseconds(receivedTime - sendTime);
        }
        catch (Exception e)
        {
            Log.Error("[SlpClient] Failed to parse server ping payload: {0}", e);
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

static class MotdUtils
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

public class SlpStatus
{
    public PingPayload Payload { get; set; }
    public TimeSpan Latency { get; set; }

    public SlpStatus(PingPayload payload, TimeSpan latency)
    {
        Payload = payload;
        Latency = latency;
    }
}

public class SlpLegacyStatus
{
    public int? PingVersion { get; set; }
    public int? ProtocolVersion { get; set; }
    public string? GameVersion { get; set; }

    public string Motd { get; set; }
    public int PlayersOnline { get; set; }
    public int MaxPlayers { get; set; }

    public SlpLegacyStatus(string motd, int playersOnline, int maxPlayers)
    {
        Motd = motd;
        PlayersOnline = playersOnline;
        MaxPlayers = maxPlayers;
    }
}

/// <summary>
/// C# represenation of the following JSON file
/// https://gist.github.com/thinkofdeath/6927216
/// </summary>
public class PingPayload
{
    /// <summary>
    /// Protocol that the server is using and the given name
    /// </summary>
    [JsonProperty(PropertyName = "version")]
    public VersionPayload Version { get; set; }

    [JsonProperty(PropertyName = "players")]
    public PlayersPayload Players { get; set; }

    [JsonProperty(PropertyName = "description")]
    public string Motd { get; set; }

    /// <summary>
    /// Server icon, important to note that it's encoded in base 64
    /// </summary>
    [JsonProperty(PropertyName = "favicon")]
    public string Icon { get; set; }
}

public class VersionPayload
{
    [JsonProperty(PropertyName = "protocol")]
    public int Protocol { get; set; }

    [JsonProperty(PropertyName = "name")] public string Name { get; set; }
}

public class PlayersPayload
{
    [JsonProperty(PropertyName = "max")] public int Max { get; set; }

    [JsonProperty(PropertyName = "online")]
    public int Online { get; set; }

    [JsonProperty(PropertyName = "sample")]
    public List<Player> Sample { get; set; }
}

public class Player
{
    [JsonProperty(PropertyName = "name")] public string Name { get; set; }

    [JsonProperty(PropertyName = "id")] public string Id { get; set; }
}

#endregion