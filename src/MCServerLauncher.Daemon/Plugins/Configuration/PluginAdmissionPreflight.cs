using System.Collections.Immutable;
using System.Globalization;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.API.Protocol;

namespace MCServerLauncher.Daemon.Plugins.Configuration;

internal enum PluginPreflightDecision
{
    Deny,
    Approve,
    ApprovePermanent,
}

internal sealed record PluginPreflightRequest(
    string PluginId,
    ImmutableArray<PluginFeatureDescriptor> Requested,
    ImmutableArray<PluginFeatureDescriptor> OutsideCeiling,
    ImmutableArray<PluginFeatureDescriptor> MissingFromEffectiveGrants,
    bool IsAdmissionDrift);

internal sealed record PluginPreflightOutcome(bool IsAdmitted, string Code, string Message)
{
    internal static PluginPreflightOutcome Admit(string code) => new(true, code, string.Empty);

    internal static PluginPreflightOutcome Skip(string code, string message) => new(false, code, message);
}

internal interface IPluginPreflightConsole
{
    bool IsInteractive { get; }

    PluginPreflightDecision Prompt(PluginPreflightRequest request);
}

internal interface IPluginAdmissionStore
{
    bool TryPersistPermanent(
        PluginManifest manifest,
        ImmutableArray<PluginFeature> expandedGrants,
        DateTimeOffset decidedAt);
}

internal sealed class PluginAdmissionPreflight
{
    private readonly DaemonPluginsConfig _config;
    private readonly IPluginPreflightConsole _console;
    private readonly IPluginAdmissionStore? _store;
    private readonly TimeProvider _timeProvider;

    internal PluginAdmissionPreflight(
        DaemonPluginsConfig config,
        IPluginPreflightConsole console,
        IPluginAdmissionStore? store = null,
        TimeProvider? timeProvider = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _store = store;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _ = PluginAdmissionPolicy.ParseGrantLevel(_config.GrantLevel);
    }

    internal static PluginAdmissionPreflight CreateNonInteractive(DaemonPluginsConfig config) =>
        new(config, NonInteractivePluginPreflightConsole.Instance);

    internal PluginPreflightOutcome Evaluate(PluginManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var effectiveGrant = PluginAdmissionPolicy.Decide(manifest, _config);
        if (!effectiveGrant.Enabled)
            return PluginPreflightOutcome.Skip("entry_disabled", "The plugin is disabled by config.");

        var level = PluginAdmissionPolicy.ParseGrantLevel(_config.GrantLevel);
        var baseline = PluginAdmissionPolicy.FeaturesAllowedByLevel(level, _config).ToHashSet();
        var outsideCeiling = manifest.Features
            .Where(feature => !baseline.Contains(feature))
            .OrderBy(static feature => feature.Value, StringComparer.Ordinal)
            .ToImmutableArray();

        var hasAdmission = _config.Admissions.TryGetValue(manifest.Identity.Id, out var admission);
        var admissionMatches = hasAdmission &&
            PluginAdmissionPolicy.AdmissionMatches(admission!, manifest.ManifestDigest, manifest.Features);
        if (admissionMatches && effectiveGrant.Denied.IsEmpty)
            return PluginPreflightOutcome.Admit("permanent_admission_reused");

        var isAdmissionDrift = hasAdmission && !admissionMatches;
        if (outsideCeiling.IsEmpty && !isAdmissionDrift)
            return PluginPreflightOutcome.Admit("within_ceiling");

        if (!_console.IsInteractive)
        {
            return PluginPreflightOutcome.Skip(
                isAdmissionDrift ? "admission_drift_non_interactive" : "approval_required_non_interactive",
                isAdmissionDrift
                    ? "The permanent admission no longer matches the manifest; an interactive re-review is required."
                    : "The plugin requires features above the configured ceiling; interactive approval is required.");
        }

        var requested = Describe(manifest.Features);
        var outsideDescriptors = Describe(outsideCeiling);
        var missing = Describe(effectiveGrant.Denied);
        var decision = _console.Prompt(new PluginPreflightRequest(
            manifest.Identity.Id,
            requested,
            outsideDescriptors,
            missing,
            isAdmissionDrift));

        return decision switch
        {
            PluginPreflightDecision.Deny => PluginPreflightOutcome.Skip(
                "approval_denied",
                "The operator denied this plugin admission."),
            PluginPreflightDecision.Approve => PluginPreflightOutcome.Admit("approved_for_process"),
            PluginPreflightDecision.ApprovePermanent => PersistPermanent(manifest, outsideCeiling),
            _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, "Unknown plugin preflight decision."),
        };
    }

    private PluginPreflightOutcome PersistPermanent(
        PluginManifest manifest,
        ImmutableArray<PluginFeature> outsideCeiling)
    {
        if (_store is null || !_store.TryPersistPermanent(
                manifest,
                outsideCeiling,
                _timeProvider.GetUtcNow()))
        {
            return PluginPreflightOutcome.Skip(
                "permanent_admission_persist_failed",
                "The permanent admission could not be persisted atomically.");
        }

        return PluginPreflightOutcome.Admit("approved_permanently");
    }

    private static ImmutableArray<PluginFeatureDescriptor> Describe(IEnumerable<PluginFeature> features)
    {
        var builder = ImmutableArray.CreateBuilder<PluginFeatureDescriptor>();
        foreach (var feature in features)
        {
            if (FeatureCatalog.TryGet(feature.Value, out var descriptor))
                builder.Add(descriptor);
        }

        return builder
            .OrderBy(static descriptor => descriptor.Feature.Value, StringComparer.Ordinal)
            .ToImmutableArray();
    }
}

internal sealed class SystemPluginPreflightConsole : IPluginPreflightConsole
{
    internal static SystemPluginPreflightConsole Instance { get; } = new();

    private SystemPluginPreflightConsole()
    {
    }

    public bool IsInteractive =>
        Environment.UserInteractive && !System.Console.IsInputRedirected && !System.Console.IsOutputRedirected;

    public PluginPreflightDecision Prompt(PluginPreflightRequest request)
    {
        System.Console.WriteLine($"Plugin '{request.PluginId}' requires admission review.");
        foreach (var descriptor in request.OutsideCeiling)
            System.Console.WriteLine($"  {descriptor.Feature.Value} ({descriptor.Risk})");

        while (true)
        {
            System.Console.Write("Deny [d], approve once [a], or approve permanently [p]: ");
            var response = System.Console.ReadLine();
            if (response is null)
                return PluginPreflightDecision.Deny;

            switch (response.Trim().ToLowerInvariant())
            {
                case "d":
                case "deny":
                    return PluginPreflightDecision.Deny;
                case "a":
                case "approve":
                    return PluginPreflightDecision.Approve;
                case "p":
                case "permanent":
                    return PluginPreflightDecision.ApprovePermanent;
            }
        }
    }
}

internal sealed class NonInteractivePluginPreflightConsole : IPluginPreflightConsole
{
    internal static NonInteractivePluginPreflightConsole Instance { get; } = new();

    private NonInteractivePluginPreflightConsole()
    {
    }

    public bool IsInteractive => false;

    public PluginPreflightDecision Prompt(PluginPreflightRequest request) =>
        throw new InvalidOperationException("A non-interactive preflight console cannot prompt.");
}

internal sealed class AppConfigPluginAdmissionStore
    : IPluginAdmissionStore
{
    private readonly object _gate = new();
    private readonly AppConfig _appConfig;
    private readonly string? _configPath;

    internal AppConfigPluginAdmissionStore(AppConfig appConfig, string? configPath = null)
    {
        _appConfig = appConfig ?? throw new ArgumentNullException(nameof(appConfig));
        _configPath = configPath;
    }

    public bool TryPersistPermanent(
        PluginManifest manifest,
        ImmutableArray<PluginFeature> expandedGrants,
        DateTimeOffset decidedAt)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        lock (_gate)
        {
            var id = manifest.Identity.Id;
            var hadGrants = _appConfig.Plugins.PluginGrants.TryGetValue(id, out var previousGrants);
            var grantsSnapshot = previousGrants?.ToArray();
            var hadAdmission = _appConfig.Plugins.Admissions.TryGetValue(id, out var previousAdmission);

            var mergedGrants = new SortedSet<string>(previousGrants ?? [], StringComparer.Ordinal);
            foreach (var feature in expandedGrants)
                mergedGrants.Add(feature.Value);

            _appConfig.Plugins.PluginGrants[id] = mergedGrants.ToArray();
            _appConfig.Plugins.Admissions[id] = new PluginAdmissionConfig
            {
                Decision = "allow",
                ManifestDigest = manifest.ManifestDigest,
                Features = manifest.Features
                    .Select(static feature => feature.Value)
                    .Order(StringComparer.Ordinal)
                    .ToArray(),
                DecidedAt = decidedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            };

            if (_appConfig.TrySave(_configPath))
                return true;

            if (hadGrants)
                _appConfig.Plugins.PluginGrants[id] = grantsSnapshot!;
            else
                _appConfig.Plugins.PluginGrants.Remove(id);
            if (hadAdmission)
                _appConfig.Plugins.Admissions[id] = previousAdmission!;
            else
                _appConfig.Plugins.Admissions.Remove(id);
            return false;
        }
    }
}
