using System.Net;
using System.Net.Sockets;
using MCServerLauncher.Common.Network;

namespace MCServerLauncher.ProtocolTests;

public class SlpClientFailureToleranceTests
{
    [Fact]
    public async Task GetStatusModern_InvalidStatusPayload_ReturnsNullWithoutLatencyWrite()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            client.LingerState = new LingerOption(true, 0);

            using var stream = client.GetStream();
            var buffer = new byte[512];
            await stream.ReadAtLeastAsync(buffer, 1);

            await stream.WriteAsync(new byte[] { 0x02, 0x00, 0x00 });
        });

        var result = await SlpClient.GetStatusModern(IPAddress.Loopback.ToString(), port);

        Assert.Null(result);
        await serverTask.WaitAsync(TimeSpan.FromSeconds(3));
    }
}
