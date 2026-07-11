using System.Text.Json;
using MCServerLauncher.Common.Contracts.System;
using MCServerLauncher.Common.ProtoType.Notification;
using MCServerLauncher.Common.ProtoType.Status;
using MCServerLauncher.Daemon.ApplicationCore.Events;
using MCServerLauncher.Daemon.Serialization;
using Microsoft.Extensions.Logging;
using TouchSocket.Core;
using TouchSocket.Http.WebSockets;

namespace MCServerLauncher.Daemon.Remote.Event;

internal sealed class LegacyDomainEventAdapter : IDisposable
{
    private readonly WsContextContainer _wsContexts;
    private readonly ILogger<LegacyDomainEventAdapter> _logger;
    private readonly IDisposable _logSubscription;
    private readonly IDisposable _reportSubscription;
    private readonly IDisposable _notificationSubscription;

    public LegacyDomainEventAdapter(
        IDomainEventPort domainEvents,
        IEventService eventService,
        WsContextContainer wsContexts,
        ILogger<LegacyDomainEventAdapter> logger)
    {
        _wsContexts = wsContexts;
        _logger = logger;
        _logSubscription = domainEvents.Subscribe<InstanceLogDomainEvent>(domainEvent =>
            eventService.OnInstanceLog(domainEvent.InstanceId, domainEvent.Log));
        _reportSubscription = domainEvents.Subscribe<DaemonReportDomainEvent>(domainEvent =>
            eventService.OnDaemonReport(ToLegacyReport(domainEvent)));
        _notificationSubscription = domainEvents.Subscribe<ClientNotificationDomainEvent>(OnClientNotification);
    }

    public void Dispose()
    {
        Stop();
    }

    public void Stop()
    {
        _logSubscription.Dispose();
        _reportSubscription.Dispose();
        _notificationSubscription.Dispose();
    }

    private void OnClientNotification(ClientNotificationDomainEvent domainEvent)
    {
        _ = FanOutNotificationAsync(domainEvent);
    }

    private async Task FanOutNotificationAsync(ClientNotificationDomainEvent domainEvent)
    {
        try
        {
            var packet = new NotificationPacket
            {
                Title = domainEvent.Title,
                Message = domainEvent.Message,
                Severity = domainEvent.Severity,
                SourceInstanceId = domainEvent.SourceInstanceId,
                RuleId = domainEvent.RuleId,
                Timestamp = domainEvent.Timestamp
            };
            var payload = JsonSerializer.SerializeToUtf8Bytes(packet, DaemonRpcTypeInfoCache<NotificationPacket>.TypeInfo);
            await Task.WhenAll(_wsContexts.Select(context => SendTextFrameAsync(context.Value.GetWebsocket(), payload)));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to fan out a legacy notification event");
        }
    }

    private static DaemonReport ToLegacyReport(DaemonReportDomainEvent domainEvent)
    {
        var report = domainEvent.SystemInfo;
        return new DaemonReport(
            new OsInfo(report.Os.Name, report.Os.Architecture),
            new CpuInfo(report.Cpu.Vendor, report.Cpu.Name, report.Cpu.Count, report.Cpu.Usage),
            new MemInfo(report.Mem.TotalKilobytes, report.Mem.FreeKilobytes),
            ToLegacyDrive(report.Drive),
            domainEvent.StartTimestamp,
            report.Drives.Select(ToLegacyDrive).ToArray(),
            report.DaemonVersion);
    }

    private static DriveInformation ToLegacyDrive(MCServerLauncher.Common.Contracts.System.DriveInfo drive)
    {
        return new DriveInformation(drive.DriveFormat, drive.TotalBytes, drive.FreeBytes, drive.Name);
    }

    private static async Task SendTextFrameAsync(IWebSocket webSocket, byte[] utf8Payload)
    {
        var frame = new WSDataFrame(utf8Payload)
        {
            Opcode = WSDataType.Text,
            FIN = true
        };
        await webSocket.SendAsync(frame);
    }
}
