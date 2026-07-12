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

internal sealed class LegacyDomainEventAdapter : IDisposable, IAsyncDisposable
{
    private readonly IDomainEventPort _domainEvents;
    private readonly WsContextContainer _wsContexts;
    private readonly DomainEventOwner _eventOwner;
    private readonly OwnedTaskSupervisor _supervisor;
    private int _stopped;

    public LegacyDomainEventAdapter(
        IDomainEventPort domainEvents,
        IEventService eventService,
        WsContextContainer wsContexts,
        ILogger<LegacyDomainEventAdapter> logger)
    {
        _domainEvents = domainEvents;
        _wsContexts = wsContexts;
        _eventOwner = domainEvents.CreateOwner(nameof(LegacyDomainEventAdapter));
        _supervisor = new OwnedTaskSupervisor(nameof(LegacyDomainEventAdapter), logger);
        domainEvents.Subscribe<InstanceLogDomainEvent>(_eventOwner, (domainEvent, _) =>
        {
            eventService.OnInstanceLog(domainEvent.InstanceId, domainEvent.Log);
            return ValueTask.CompletedTask;
        });
        domainEvents.Subscribe<DaemonReportDomainEvent>(_eventOwner, (domainEvent, _) =>
        {
            eventService.OnDaemonReport(ToLegacyReport(domainEvent));
            return ValueTask.CompletedTask;
        });
        domainEvents.Subscribe<ClientNotificationDomainEvent>(_eventOwner, OnClientNotification);
    }

    public void Dispose()
    {
        Stop();
    }

    public void Stop()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
            return;

        _domainEvents.DisposeOwner(_eventOwner);
        _supervisor.RequestStop();
    }

    internal async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Stop();
        await _supervisor.DrainAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        Stop();
        await _supervisor.DisposeAsync();
    }

    private ValueTask OnClientNotification(
        ClientNotificationDomainEvent domainEvent,
        CancellationToken cancellationToken)
    {
        _supervisor.Schedule(
            $"notification:{domainEvent.RuleId}",
            token => FanOutNotificationAsync(domainEvent, token),
            cancellationToken);
        return ValueTask.CompletedTask;
    }

    private async Task FanOutNotificationAsync(
        ClientNotificationDomainEvent domainEvent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var packet = new NotificationPacket
        {
            Title = domainEvent.Title,
            Message = domainEvent.Message,
            Severity = domainEvent.Severity,
            SourceInstanceId = domainEvent.SourceInstanceId,
            RuleId = domainEvent.RuleId,
            Timestamp = domainEvent.Timestamp
        };
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            packet,
            DaemonRpcTypeInfoCache<NotificationPacket>.TypeInfo);
        await Task.WhenAll(_wsContexts.Select(context =>
            SendTextFrameAsync(context.Value.GetWebsocket(), payload, cancellationToken)));
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

    private static async Task SendTextFrameAsync(
        IWebSocket webSocket,
        byte[] utf8Payload,
        CancellationToken cancellationToken)
    {
        var frame = new WSDataFrame(utf8Payload)
        {
            Opcode = WSDataType.Text,
            FIN = true
        };
        await webSocket.SendAsync(frame, cancellationToken: cancellationToken);
    }
}
