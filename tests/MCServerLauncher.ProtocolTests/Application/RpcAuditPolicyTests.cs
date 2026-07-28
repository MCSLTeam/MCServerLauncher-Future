using MCServerLauncher.Common.Contracts.Backup;
using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.Contracts.Auth;
using MCServerLauncher.Common.Contracts.Provisioning;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.ApplicationCore.Audit;

namespace MCServerLauncher.ProtocolTests;

public sealed class RpcAuditPolicyTests
{
    [Fact]
    public void EveryBuiltInMethodIsClassifiedExactlyOnce()
    {
        // A new built-in RPC must be declared audited or read-only deliberately; this assertion is
        // the drift gate.
        var catalog = BuiltInProtocolDefinitions.Rpcs
            .Select(static descriptor => descriptor.Method.Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Empty(RpcAuditPolicy.AuditedMethods.Intersect(RpcAuditPolicy.ReadOnlyMethods));
        var classified = RpcAuditPolicy.AuditedMethods.Union(RpcAuditPolicy.ReadOnlyMethods)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal([], classified.Except(catalog).Order(StringComparer.Ordinal));
        Assert.Equal([], catalog.Except(classified).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void CreateEvent_ExtractsIdentifyingMetadataButNeverPayloadContent()
    {
        var instanceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var command = RpcAuditPolicy.CreateEvent(
            "mcsl.instance.command.send",
            "mcsl.instance.command.send",
            "user-a",
            null,
            new InstanceCommandRequest(instanceId, "op secret-word"),
            result: null,
            succeeded: true,
            errorCode: null);
        Assert.Equal(instanceId.ToString("D"), command.Target);
        Assert.DoesNotContain(
            new[] { command.Target, command.Method, command.Permission, command.ErrorCode, command.ConfirmedBy },
            value => value?.Contains("secret-word", StringComparison.Ordinal) == true);

        var token = RpcAuditPolicy.CreateEvent(
            "mcsl.auth.token.issue",
            "mcsl.auth.token.issue",
            "daemon-main",
            null,
            new TokenIssueRequest("ci-bot", "mcsl://daemon/api/v2", ["mcsl.instance.start"], 3600),
            new TokenIssueResult("jwt-token-material", "ci-bot", "mcsl://daemon/api/v2", ["mcsl.instance.start"], DateTimeOffset.UnixEpoch, "token-id"),
            succeeded: true,
            errorCode: null);
        Assert.Equal("ci-bot", token.Target);
        Assert.DoesNotContain(
            new[] { token.Target, token.PlanHash, token.ErrorCode, token.ConfirmedBy },
            value => value?.Contains("jwt-token-material", StringComparison.Ordinal) == true);

        var transfer = RpcAuditPolicy.CreateEvent(
            "mcsl.file.move",
            "mcsl.file.move",
            "user-a",
            null,
            new PathTransferRequest("a/level.dat", "b/level.dat"),
            result: null,
            succeeded: false,
            errorCode: "file.not_found");
        Assert.Equal("a/level.dat -> b/level.dat", transfer.Target);
        Assert.False(transfer.Succeeded);
        Assert.Equal("file.not_found", transfer.ErrorCode);
    }

    [Fact]
    public void CreateEvent_ResultIdentifiersRefineRequestIdentifiers()
    {
        var planId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var operationId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        var confirm = RpcAuditPolicy.CreateEvent(
            "mcsl.backup.restore.confirm",
            "mcsl.backup.restore.confirm",
            "user-a",
            null,
            new BackupRestoreConfirmRequest(planId, "hash-echo", "user-a"),
            result: null,
            succeeded: false,
            errorCode: "plan.hash_mismatch");
        Assert.Equal(planId, confirm.PlanId);
        Assert.Equal("hash-echo", confirm.PlanHash);

        var executed = RpcAuditPolicy.CreateEvent(
            "mcsl.provisioning.execute",
            "mcsl.provisioning.execute",
            "user-a",
            null,
            new ProvisioningExecuteRequest(planId, "user-a"),
            new ProvisioningExecuteResult(planId, operationId),
            succeeded: true,
            errorCode: null);
        Assert.Equal(planId, executed.PlanId);
        Assert.Equal(operationId, executed.OperationId);

        var backupExecuted = RpcAuditPolicy.CreateEvent(
            "mcsl.backup.restore.execute",
            "mcsl.backup.restore.execute",
            "user-a",
            null,
            new BackupRestoreExecuteRequest(planId, "user-a"),
            new BackupRestoreExecuteResult(planId, operationId),
            succeeded: true,
            errorCode: null);
        Assert.Equal(planId, backupExecuted.PlanId);
        Assert.Equal(operationId, backupExecuted.OperationId);
    }
}
