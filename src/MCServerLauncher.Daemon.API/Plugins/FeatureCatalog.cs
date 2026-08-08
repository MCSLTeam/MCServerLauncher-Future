using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using MCServerLauncher.Daemon.API.Protocol;

namespace MCServerLauncher.Daemon.API.Plugins;

/// <summary>
/// Risk class for plugin features used by admission preflight.
/// </summary>
public enum PluginFeatureRisk
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3,
}

/// <summary>
/// Authoritative catalog entry for one plugin feature.
/// </summary>
public sealed record PluginFeatureDescriptor(
    PluginFeature Feature,
    string Summary,
    PluginFeatureRisk Risk,
    ImmutableArray<string> Methods,
    bool IsImplemented);

/// <summary>
/// Daemon-owned feature vocabulary: summary, risk, host method expansion, and
/// whether the current host implements the feature surface.
/// </summary>
public static class FeatureCatalog
{
    private static readonly ImmutableArray<PluginFeatureDescriptor> AllDescriptors = CreateAll();
    private static readonly ImmutableDictionary<string, PluginFeatureDescriptor> Lookup = CreateLookup(AllDescriptors);

    public static ImmutableArray<PluginFeatureDescriptor> All => AllDescriptors;

    public static bool TryGet(string value, [NotNullWhen(true)] out PluginFeatureDescriptor? descriptor) =>
        Lookup.TryGetValue(value, out descriptor);

    public static bool IsKnown(string value) => Lookup.ContainsKey(value);

    public static bool IsImplemented(PluginFeature feature) =>
        Lookup.TryGetValue(feature.Value, out var descriptor) && descriptor.IsImplemented;

    public static ImmutableArray<string> MethodsFor(PluginFeature feature) =>
        Lookup.TryGetValue(feature.Value, out var descriptor)
            ? descriptor.Methods
            : ImmutableArray<string>.Empty;

    public static ImmutableArray<PluginFeature> FeaturesAllowedByRisk(PluginFeatureRisk maximumRisk)
    {
        var builder = ImmutableArray.CreateBuilder<PluginFeature>();
        foreach (var descriptor in AllDescriptors)
        {
            if (descriptor.IsImplemented && descriptor.Risk <= maximumRisk)
                builder.Add(descriptor.Feature);
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Expands granted feature ids into the union of implemented host method permissions.
    /// Unknown or unimplemented features contribute nothing.
    /// </summary>
    public static ImmutableArray<string> ExpandMethodPermissions(IEnumerable<string> grantedFeatureIds)
    {
        ArgumentNullException.ThrowIfNull(grantedFeatureIds);
        var set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var featureId in grantedFeatureIds)
        {
            if (string.IsNullOrWhiteSpace(featureId))
                continue;
            if (!Lookup.TryGetValue(featureId, out var descriptor) || !descriptor.IsImplemented)
                continue;
            foreach (var method in descriptor.Methods)
                set.Add(method);
        }

        return set.ToImmutableArray();
    }

    private static ImmutableArray<PluginFeatureDescriptor> CreateAll() =>
    [
        Descriptor(PluginFeature.RpcRegister, "Register typed RPC methods into the daemon catalog.", PluginFeatureRisk.Medium, [], implemented: true),
        Descriptor(PluginFeature.EventPublish, "Publish typed plugin events into the daemon event bus.", PluginFeatureRisk.Medium, [], implemented: true),
        Descriptor(
            PluginFeature.EventSubscribe,
            "Subscribe to typed application events.",
            PluginFeatureRisk.Medium,
            [],
            implemented: true),
        Descriptor(
            PluginFeature.InstanceQuery,
            "Read instance catalog, reports, logs, and settings.",
            PluginFeatureRisk.None,
            [
                "mcsl.instance.catalog.get",
                "mcsl.instance.report.get",
                "mcsl.instance.report.list",
                "mcsl.instance.log.get",
                "mcsl.instance.settings.get",
            ],
            implemented: true),
        Descriptor(
            PluginFeature.InstanceManage,
            "Create and mutate instance lifecycle, commands, and settings.",
            PluginFeatureRisk.Medium,
            [
                "mcsl.instance.create",
                "mcsl.instance.start",
                "mcsl.instance.stop",
                "mcsl.instance.halt",
                "mcsl.instance.remove",
                "mcsl.instance.command.send",
                "mcsl.instance.settings.update",
            ],
            implemented: true),
        Descriptor(
            PluginFeature.FileRead,
            "Read contained instance filesystem metadata and file contents.",
            PluginFeatureRisk.Low,
            [
                "mcsl.directory.info.get", "mcsl.file.info.get",
                "mcsl.file.download.open", "mcsl.file.download.read", "mcsl.file.download.close"
            ],
            implemented: true),
        // The upload chunk write carries no method name of its own; it is authorized by the open
        // permission, exactly as the wire authorizes its binary frames by the open lease.
        Descriptor(
            PluginFeature.FileWrite,
            "Create, upload, move, rename, or delete contained instance paths.",
            PluginFeatureRisk.Medium,
            [
                "mcsl.directory.create", "mcsl.directory.copy", "mcsl.directory.delete",
                "mcsl.directory.move", "mcsl.directory.rename",
                "mcsl.file.copy", "mcsl.file.delete", "mcsl.file.move", "mcsl.file.rename",
                "mcsl.file.upload.open", "mcsl.file.upload.close", "mcsl.file.upload.cancel"
            ],
            implemented: true),
        Descriptor(
            PluginFeature.SystemQuery,
            "Read host system facts and discovered Java runtimes.",
            PluginFeatureRisk.None,
            ["mcsl.system.info.get", "mcsl.java.list"],
            implemented: true),
        Descriptor(
            PluginFeature.EventRuleManage,
            "Read, validate, test, and update event rules.",
            PluginFeatureRisk.Medium,
            ["mcsl.instance.event-rules.get", "mcsl.instance.event-rules.update"],
            implemented: true),
        Descriptor(
            PluginFeature.OperationQuery,
            "List and read immutable long-running operation snapshots.",
            PluginFeatureRisk.None,
            ["mcsl.operation.list", "mcsl.operation.get"],
            implemented: true),
        Descriptor(
            PluginFeature.OperationCancel,
            "Request cooperative cancellation of long-running operations.",
            PluginFeatureRisk.Medium,
            ["mcsl.operation.cancel"],
            implemented: true),
        Descriptor(
            PluginFeature.ProvisioningManage,
            "Resolve and execute immutable provisioning plans.",
            PluginFeatureRisk.Medium,
            ["mcsl.provisioning.resolve", "mcsl.provisioning.get", "mcsl.provisioning.execute"],
            implemented: true),
        Descriptor(
            PluginFeature.BackupManage,
            "List, create, prune, and restore cold backups.",
            PluginFeatureRisk.Medium,
            [
                "mcsl.backup.list", "mcsl.backup.create", "mcsl.backup.prune",
                "mcsl.backup.restore.plan", "mcsl.backup.restore.confirm", "mcsl.backup.restore.execute"
            ],
            implemented: true),
        Descriptor(
            PluginFeature.MonitoringQuery,
            "Read retained system and instance metrics.",
            PluginFeatureRisk.None,
            ["mcsl.monitoring.current.get", "mcsl.monitoring.query"],
            implemented: true),
        Descriptor(
            PluginFeature.AutomationManage,
            "Validate, test, apply, and enable typed automation policies.",
            PluginFeatureRisk.Medium,
            [
                "mcsl.automation.get", "mcsl.automation.validate", "mcsl.automation.test",
                "mcsl.automation.apply", "mcsl.automation.enable",
                "mcsl.automation.intent.confirm", "mcsl.automation.intent.execute"
            ],
            implemented: true),
        Descriptor(
            PluginFeature.AuditQuery,
            "Query bounded structured audit records.",
            PluginFeatureRisk.None,
            ["mcsl.audit.query"],
            implemented: true),
        Descriptor(PluginFeature.StoragePrivate, "Read and write plugin-private bounded storage.", PluginFeatureRisk.Low, [], implemented: true),
        Descriptor(PluginFeature.NetworkHttpListen, "Validate and open plugin-owned HTTP listeners.", PluginFeatureRisk.High, [], implemented: true),
        Descriptor(PluginFeature.AuthVerify, "Verify audience-bound daemon tokens into principals.", PluginFeatureRisk.Medium, [], implemented: true),
    ];

    private static ImmutableDictionary<string, PluginFeatureDescriptor> CreateLookup(
        ImmutableArray<PluginFeatureDescriptor> descriptors)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, PluginFeatureDescriptor>(StringComparer.Ordinal);
        foreach (var descriptor in descriptors)
            builder.Add(descriptor.Feature.Value, descriptor);
        return builder.ToImmutable();
    }

    private static PluginFeatureDescriptor Descriptor(
        PluginFeature feature,
        string summary,
        PluginFeatureRisk risk,
        string[] methods,
        bool implemented) =>
        new(feature, summary, risk, methods.ToImmutableArray(), implemented);
}
