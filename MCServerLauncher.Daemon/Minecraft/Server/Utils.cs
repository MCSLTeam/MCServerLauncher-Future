using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Serilog;

namespace MCServerLauncher.Daemon.Minecraft.Server;

public static class Utils
{
    #region Get Process Environment Variables

    // ================================ Get Process Environment Variables ================================
    // PInvoke declarations
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr processHandle, int processInformationClass,
        ref ProcessBasicInformation processInformation, uint processInformationLength, ref uint returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, [Out] byte[] lpBuffer,
        int dwSize,
        out int lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private static IEnumerable<string> GetEnvironmentVariablesWin(int pid)
    {
        var processHandle =
            OpenProcess(0x0010 | 0x0400 | 0x0008, false, pid); // PROCESS_QUERY_INFORMATION | PROCESS_VM_READ

        if (processHandle == IntPtr.Zero)
        {
            Console.WriteLine("Failed to open process.");
            return Enumerable.Empty<string>();
        }

        var pbi = new ProcessBasicInformation();
        uint returnLength = 0;
        var status =
            NtQueryInformationProcess(processHandle, 0, ref pbi, (uint)Marshal.SizeOf(pbi), ref returnLength);

        if (status != 0)
        {
            Console.WriteLine("Failed to query process information.");
            CloseHandle(processHandle);
            return Enumerable.Empty<string>();
        }

        var pebAddress = pbi.PebBaseAddress;

        // Read PEB memory
        var pebBuffer = new byte[IntPtr.Size];
        int bytesRead;
        if (!ReadProcessMemory(processHandle, pebAddress + 0x20 /* Offset for ProcessParameters */, pebBuffer,
                pebBuffer.Length, out bytesRead))
        {
            Console.WriteLine("Failed to read PEB.");
            CloseHandle(processHandle);
            return Enumerable.Empty<string>();
        }

        var processParametersAddress = (IntPtr)BitConverter.ToInt64(pebBuffer, 0);

        // Read environment variables block address
        var environmentBuffer = new byte[IntPtr.Size];
        if (!ReadProcessMemory(processHandle, processParametersAddress + 0x80 /* Offset for Environment */,
                environmentBuffer, environmentBuffer.Length, out bytesRead))
        {
            Console.WriteLine("Failed to read process parameters.");
            CloseHandle(processHandle);
            return Enumerable.Empty<string>();
        }

        var environmentAddress = (IntPtr)BitConverter.ToInt64(environmentBuffer, 0);

        // Read the environment block (arbitrary large buffer to read environment variables)
        var environmentData = new byte[0x4000]; // Adjust size if needed
        if (!ReadProcessMemory(processHandle, environmentAddress, environmentData, environmentData.Length,
                out bytesRead))
        {
            Console.WriteLine("Failed to read environment block.");
            CloseHandle(processHandle);
            return Enumerable.Empty<string>();
        }

        // Convert environment data to string and split by null terminators
        var environmentString = Encoding.Unicode.GetString(environmentData).Trim();

        // split \0\0
        environmentString = environmentString.Substring(0, FindEnvironStringEnd(environmentString));

        var environmentVariables =
            environmentString.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);

        CloseHandle(processHandle);
        return environmentVariables;
    }

    /// <summary>
    ///     查找'\0''\0'的位置
    /// </summary>
    /// <param name="environ"></param>
    /// <returns></returns>
    private static int FindEnvironStringEnd(string environ)
    {
        var lastIndex = environ.IndexOf('\0');
        while (lastIndex != -1)
        {
            var index = environ.IndexOf('\0', lastIndex + 1);
            if (index == lastIndex + 1) return lastIndex;

            lastIndex = index;
        }

        return -1;
    }

    private static IEnumerable<string> GetEnvironmentVariablesLinux(int pid)
    {
        var process = Process.Start("cat", $"/proc/{pid}/environ");
        process?.WaitForExit();
        return process?.StandardOutput.ReadToEnd().Split('\0').ToList() ?? Enumerable.Empty<string>();
    }

    public static IEnumerable<string> GetEnvironmentVariables(int pid)
    {
        return GetEnvironmentVariablesWin(pid);
    }

    // Struct to hold process information
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2_0;
        public IntPtr Reserved2_1;
        public IntPtr UniqueProcessId;
        public IntPtr Reserved3;
    }
    // ================================ Get Process Environment Variables ================================

    #endregion

    #region Server Ping List Utils

    public class SlpClient
    {
        public bool Connected => Client.Connected;
        private List<byte> Buffer { get; } = new();
        private TcpClient Client { get; } = new();

        /// <summary>
        ///  获取SLP的status和ping信息,用于1.7以上
        /// </summary>
        /// <param name="host"></param>
        /// <param name="port"></param>
        /// <param name="cancellationToken"></param>
        /// <returns>(status包信息, 延迟)</returns>
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
            await client.ConnectAsync(host, port, cancellationToken);
            if (!client.Connected) return null;

            await using var stream = client.GetStream();

            // send request
            await stream.WriteAsync(new byte[] { 0xFE, 0x01 }, cancellationToken);

            // read response
            var buffer = new byte[2048];
            var length = await stream.ReadAsync(buffer, cancellationToken);

            // validate response
            if (buffer[0] != 0xFF)
            {
                Log.Error("[SlpClient] Received invalid packet");
                return null;
            }

            var payload = Encoding.BigEndianUnicode.GetString(buffer, 3, length - 3);
            if (payload.StartsWith("§"))
            {
                var data = payload.Split("\u0000");

                var pingVersion = int.Parse(data[0][1..]);
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
                var data = payload.Split("\u0000");

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

            // implicitly convert byte[] to ReadOnlyMemory<byte>
            await stream.WriteAsync(bufferLength, cancellationToken);
            await stream.WriteAsync(packetData, cancellationToken);
            await stream.WriteAsync(data, cancellationToken);
        }

        public async Task<bool> HandShakeAsync(string host, ushort port, CancellationToken cancellationToken = default)
        {
            // create connection
            await Client.ConnectAsync(host, port, cancellationToken);
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
            var _ = await Client.GetStream().ReadAsync(received, cancellationToken); // dispose return value

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
            var _ = await Client.GetStream().ReadAsync(received, cancellationToken);
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

    #endregion
}