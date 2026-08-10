using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MCServerLauncher.WPF.Features.Plugins;

public sealed record PluginMpxPackageLimits(
    int MaxPackageBytes = 10 * 1024 * 1024,
    int MaxFiles = 64,
    int MaxPathLength = 180,
    int MaxSingleFileBytes = 2 * 1024 * 1024,
    int MaxUiBytes = 256 * 1024,
    int MaxBundleBytes = 1024 * 1024,
    int MaxResourceBytes = 2 * 1024 * 1024,
    int MaxPrivateStorageBytes = 2 * 1024 * 1024);

public sealed record PluginMpxTrustedPublisher(
    string Publisher,
    string KeyId,
    byte[] PublicKeySubjectPublicKeyInfo);

public sealed record PluginMpxSignatureTrust(
    string Publisher,
    string KeyId,
    string PublicKeySha256,
    DateTimeOffset? SignedAt);

public sealed record PluginMpxPackageValidatorOptions(
    PluginMpxPackageLimits? Limits = null,
    IReadOnlySet<string>? AllowedHostCapabilities = null,
    IReadOnlyList<PluginMpxTrustedPublisher>? TrustedPublishers = null);

public sealed record PluginMpxDiagnostic(string Code, string Path, string Message);

public sealed record PluginMpxDeploymentFile(string Path, string Sha256);

public sealed record PluginMpxDeploymentScript(string Path, string Sha256, string? Module);

public sealed record PluginMpxDeploymentResource(string Path, string Sha256, string Mime, int Bytes);

public sealed record PluginMpxExtensionPoint(string Kind, string Id, string? Target);

public sealed record PluginMpxCommand(string Id, string? Title, string? Description, string? Target);

public sealed record PluginMpxExtensionDependency(string Id, string Version);

public sealed record PluginMpxUpdatePolicy(string Channel, string Strategy);

public sealed record PluginMpxClientDeployment(
    PluginMpxDeploymentFile? Ui,
    PluginMpxDeploymentFile? Theme,
    PluginMpxDeploymentScript? Script,
    IReadOnlyList<PluginMpxDeploymentResource> Resources);

public sealed record PluginMpxDaemonDeployment(
    PluginMpxDeploymentFile? Plugin,
    IReadOnlyList<PluginMpxExtensionPoint> ExtensionPoints,
    IReadOnlyList<PluginMpxCommand> Commands);

public sealed record PluginMpxDeploymentPlan(
    IReadOnlyList<string> Scopes,
    PluginMpxClientDeployment? Client,
    PluginMpxDaemonDeployment? Daemon);

public sealed record ValidatedPluginMpxPackage(
    string PluginId,
    string Version,
    string? UiRuntimeRange,
    PluginUiSchema? UiSchema,
    PluginThemeRuntime? Theme,
    string? JavaScriptModule,
    IReadOnlyList<string> HostCapabilities,
    IReadOnlyList<string> EventPermissions,
    PluginMpxDeploymentPlan DeploymentPlan,
    IReadOnlyList<PluginMpxCommand> Commands,
    IReadOnlyList<PluginMpxExtensionDependency> Dependencies,
    PluginMpxUpdatePolicy? UpdatePolicy,
    IReadOnlyDictionary<string, string> FileDigests,
    long TotalUncompressedBytes,
    PluginMpxSignatureTrust? Signature);

public sealed record PluginMpxPackageValidationResult(
    bool Ok,
    ValidatedPluginMpxPackage? Package,
    IReadOnlyList<PluginMpxDiagnostic> Diagnostics)
{
    public static PluginMpxPackageValidationResult Success(ValidatedPluginMpxPackage package) =>
        new(true, package, []);

    public static PluginMpxPackageValidationResult Failure(IReadOnlyList<PluginMpxDiagnostic> diagnostics) =>
        new(false, null, diagnostics);
}

public static partial class PluginMpxPackageValidator
{
    private const string ManifestPath = "manifest.json";
    private const string SignatureDirectory = "signatures/";
    private const string SignatureManifestPath = "signatures/manifest.json";
    private const string SignatureAlgorithm = "ecdsa-p256-sha256";
    private const string SignaturePayloadAlgorithm = "sha256";
    private const string SignatureInputHeader = "MCSL-MPX-SIGNATURE-v1";

    private static readonly PluginMpxPackageLimits DefaultLimits = new();

    private static readonly IReadOnlySet<string> DefaultAllowedHostCapabilities = Set(
        "ui.state",
        "ui.notification",
        "daemon.system.query",
        "daemon.instance.query",
        "daemon.event.subscribe",
        "storage.private");

    private static readonly IReadOnlySet<string> AllowedEventCapabilities = Set(
        "daemon.instance.catalog.changed",
        "daemon.instance.log",
        "daemon.report",
        "daemon.notification");

    private static readonly IReadOnlySet<string> AllowedExtensionPointKinds = Set(
        "provider",
        "registry",
        "pipeline",
        "hook",
        "override",
        "ui",
        "event",
        "command");

    private static readonly IReadOnlySet<string> AllowedExtensionPointIds = Set(
        "provider.java",
        "provider.serverCore",
        "provider.backup",
        "provider.processLauncher",
        "registry.template",
        "registry.serverCore",
        "registry.icon",
        "registry.formatter",
        "registry.command",
        "pipeline.instance.create",
        "hook.instance.lifecycle",
        "override.process.launcher",
        "ui.panel",
        "event.daemon",
        "command.daemon");

    private static readonly IReadOnlySet<string> AllowedResourceMimeTypes = Set(
        "image/png",
        "image/jpeg",
        "image/webp");

    private static readonly IReadOnlySet<string> AllowedUpdateChannels = Set(
        "stable",
        "preview");

    private static readonly IReadOnlySet<string> AllowedUpdateStrategies = Set(
        "manual");

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static PluginMpxPackageValidationResult Validate(
        byte[] packageBytes,
        PluginMpxPackageValidatorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(packageBytes);
        using var stream = new MemoryStream(packageBytes, writable: false);
        return Validate(stream, options);
    }

    public static PluginMpxPackageValidationResult Validate(
        Stream packageStream,
        PluginMpxPackageValidatorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(packageStream);

        var limits = options?.Limits ?? DefaultLimits;
        var allowedHostCapabilities = options?.AllowedHostCapabilities ?? DefaultAllowedHostCapabilities;
        List<PluginMpxDiagnostic> diagnostics = [];

        if (packageStream.CanSeek && packageStream.Length > limits.MaxPackageBytes)
        {
            diagnostics.Add(Diagnostic("package_too_large", "$", $"Package exceeds {limits.MaxPackageBytes} bytes."));
            return PluginMpxPackageValidationResult.Failure(diagnostics);
        }

        Dictionary<string, byte[]> entries;
        long totalUncompressedBytes;
        try
        {
            (entries, totalUncompressedBytes) = ReadZipEntries(packageStream, limits, diagnostics);
        }
        catch (InvalidDataException ex)
        {
            diagnostics.Add(Diagnostic("zip_invalid", "$", ex.Message));
            return PluginMpxPackageValidationResult.Failure(diagnostics);
        }

        if (diagnostics.Count > 0)
            return PluginMpxPackageValidationResult.Failure(diagnostics);

        if (!entries.TryGetValue(ManifestPath, out var manifestBytes))
        {
            diagnostics.Add(Diagnostic("manifest_missing", ManifestPath, "manifest.json is required."));
            return PluginMpxPackageValidationResult.Failure(diagnostics);
        }

        var manifest = ParseManifest(manifestBytes, diagnostics);
        if (manifest is null)
            return PluginMpxPackageValidationResult.Failure(diagnostics);

        ValidateManifest(manifest, allowedHostCapabilities, limits, diagnostics);
        RejectUnexpectedSignatureEntries(entries.Keys, manifest, diagnostics);
        var signature = ValidatePackageSignature(entries, manifest, options?.TrustedPublishers, diagnostics);
        if (diagnostics.Count > 0)
            return PluginMpxPackageValidationResult.Failure(diagnostics);

        var declaredFiles = CollectDeclaredFiles(manifest, diagnostics);
        if (diagnostics.Count > 0)
            return PluginMpxPackageValidationResult.Failure(diagnostics);

        foreach (var path in entries.Keys)
        {
            if (path == ManifestPath || path.StartsWith(SignatureDirectory, StringComparison.Ordinal))
                continue;

            if (!declaredFiles.Contains(path))
                diagnostics.Add(Diagnostic("file_undeclared", path, "Payload files must be declared in manifest entry or resources."));
        }

        var packageIdentity = manifest.Package!;
        var runtime = manifest.Runtime;
        var uiRef = SelectClientUiRef(manifest);
        var themeRef = SelectClientThemeRef(manifest);
        var scriptRef = SelectScriptRef(manifest);
        var daemonPluginRef = SelectDaemonPluginRef(manifest);

        Dictionary<string, string> fileDigests = [];
        if (uiRef is not null)
            ValidatePayloadFile(entries, uiRef.Ref, uiRef.Path, limits.MaxUiBytes, fileDigests, diagnostics);
        if (themeRef is not null)
            ValidatePayloadFile(entries, themeRef.Ref, themeRef.Path, limits.MaxUiBytes, fileDigests, diagnostics);
        if (scriptRef is not null)
            ValidatePayloadFile(entries, scriptRef.Ref, scriptRef.Path, limits.MaxBundleBytes, fileDigests, diagnostics);
        if (daemonPluginRef is not null)
            ValidatePayloadFile(entries, daemonPluginRef.Ref, daemonPluginRef.Path, limits.MaxSingleFileBytes, fileDigests, diagnostics);

        if (manifest.Resources is not null)
        {
            for (var i = 0; i < manifest.Resources.Count; i++)
            {
                var resource = manifest.Resources[i];
                ValidatePayloadFile(
                    entries,
                    resource,
                    $"$.resources[{i}]",
                    Math.Min(resource.Bytes ?? 0, limits.MaxResourceBytes),
                    fileDigests,
                    diagnostics);
            }
        }

        PluginUiSchema? uiSchema = null;
        if (uiRef is not null && entries.TryGetValue(uiRef.Ref.Path!, out var uiBytes))
        {
            var parsed = PluginUiSchemaParser.ParseRuntime(Encoding.UTF8.GetString(uiBytes));
            if (parsed.Ok)
            {
                uiSchema = parsed.Schema;
                ValidateUiCommandBindings(uiSchema, manifest.Commands, diagnostics);
            }
            else
            {
                foreach (var issue in parsed.Diagnostics)
                    diagnostics.Add(Diagnostic($"ui_{issue.Code}", $"{uiRef.Path}:{issue.Path}", issue.Message));
            }
        }

        PluginThemeRuntime? theme = null;
        if (themeRef is not null && entries.TryGetValue(themeRef.Ref.Path!, out var themeBytes))
        {
            var parsed = PluginThemeRuntimeParser.ParseRuntime(Encoding.UTF8.GetString(themeBytes));
            if (parsed.Ok)
            {
                theme = parsed.Theme;
            }
            else
            {
                foreach (var issue in parsed.Diagnostics)
                    diagnostics.Add(Diagnostic($"theme_{issue.Code}", $"{themeRef.Path}:{issue.Path}", issue.Message));
            }
        }

        if (diagnostics.Count > 0)
            return PluginMpxPackageValidationResult.Failure(diagnostics);

        string? script = null;
        if (scriptRef is not null)
            script = Encoding.UTF8.GetString(entries[scriptRef.Ref.Path!]);

        var deploymentPlan = BuildDeploymentPlan(manifest, uiRef, themeRef, scriptRef, daemonPluginRef);
        var commands = new ReadOnlyCollection<PluginMpxCommand>((manifest.Commands ?? []).Select(ToCommand).ToArray());
        var dependencies = new ReadOnlyCollection<PluginMpxExtensionDependency>((manifest.Dependencies?.Extensions ?? []).Select(ToDependency).ToArray());
        var updatePolicy = ToUpdatePolicy(manifest.Updates);

        var package = new ValidatedPluginMpxPackage(
            packageIdentity.Id!,
            packageIdentity.Version!,
            runtime?.Ui,
            uiSchema,
            theme,
            script,
            new ReadOnlyCollection<string>((manifest.Permissions?.Host ?? []).ToArray()),
            new ReadOnlyCollection<string>((manifest.Permissions?.Events ?? []).ToArray()),
            deploymentPlan,
            commands,
            dependencies,
            updatePolicy,
            new ReadOnlyDictionary<string, string>(fileDigests),
            totalUncompressedBytes,
            signature);

        return PluginMpxPackageValidationResult.Success(package);
    }

    private static (Dictionary<string, byte[]> Entries, long TotalUncompressedBytes) ReadZipEntries(
        Stream packageStream,
        PluginMpxPackageLimits limits,
        List<PluginMpxDiagnostic> diagnostics)
    {
        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        long totalUncompressedBytes = 0;

        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count > limits.MaxFiles)
            diagnostics.Add(Diagnostic("file_count_exceeded", "$", $"Package file count exceeds {limits.MaxFiles}."));

        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
            {
                diagnostics.Add(Diagnostic("directory_entry_unsupported", entry.FullName, "Directory entries are not allowed in .mpx packages."));
                continue;
            }

            if (!TryNormalizePath(entry.FullName, limits, out var path, out var pathDiagnostic))
            {
                diagnostics.Add(pathDiagnostic);
                continue;
            }

            if (!entries.TryAdd(path, []))
            {
                diagnostics.Add(Diagnostic("path_duplicate", path, "Package paths must be unique."));
                continue;
            }

            if (entry.Length < 0 || entry.Length > limits.MaxSingleFileBytes)
            {
                diagnostics.Add(Diagnostic("payload_too_large", path, $"Payload exceeds {limits.MaxSingleFileBytes} bytes."));
                continue;
            }

            totalUncompressedBytes += entry.Length;
            if (totalUncompressedBytes > limits.MaxPackageBytes)
            {
                diagnostics.Add(Diagnostic("uncompressed_size_exceeded", "$", $"Uncompressed package size exceeds {limits.MaxPackageBytes} bytes."));
                continue;
            }

            entries[path] = ReadEntryBytes(entry, limits.MaxSingleFileBytes);
        }

        return (entries, totalUncompressedBytes);
    }

    private static byte[] ReadEntryBytes(ZipArchiveEntry entry, int limit)
    {
        using var source = entry.Open();
        using var output = new MemoryStream(capacity: (int)Math.Min(entry.Length, limit));
        var buffer = new byte[8192];
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (output.Length + read > limit)
                throw new InvalidDataException($"Entry '{entry.FullName}' exceeds the configured read limit.");
            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private static MpxManifestDocument? ParseManifest(byte[] bytes, List<PluginMpxDiagnostic> diagnostics)
    {
        try
        {
            return JsonSerializer.Deserialize<MpxManifestDocument>(bytes, ManifestJsonOptions);
        }
        catch (JsonException ex)
        {
            diagnostics.Add(Diagnostic("manifest_parse_failed", ManifestPath, ex.Message));
            return null;
        }
    }

    private static void ValidateManifest(
        MpxManifestDocument manifest,
        IReadOnlySet<string> allowedHostCapabilities,
        PluginMpxPackageLimits limits,
        List<PluginMpxDiagnostic> diagnostics)
    {
        if (manifest.Package is null)
        {
            diagnostics.Add(Diagnostic("package_missing", "$.package", "package is required."));
        }
        else
        {
            if (string.IsNullOrWhiteSpace(manifest.Package.Id) || !PackageIdRegex().IsMatch(manifest.Package.Id))
                diagnostics.Add(Diagnostic("package_id_invalid", "$.package.id", "Package id is invalid."));
            if (string.IsNullOrWhiteSpace(manifest.Package.Version) || !SemVerLikeRegex().IsMatch(manifest.Package.Version))
                diagnostics.Add(Diagnostic("package_version_invalid", "$.package.version", "Package version must be semver-like."));
        }

        var uiRef = SelectClientUiRef(manifest);
        var themeRef = SelectClientThemeRef(manifest);
        var daemonPluginRef = SelectDaemonPluginRef(manifest);

        if (uiRef is not null && (manifest.Runtime is null || string.IsNullOrWhiteSpace(manifest.Runtime.Ui)))
            diagnostics.Add(Diagnostic("runtime_ui_missing", "$.runtime.ui", "runtime.ui is required when targets.client.ui is declared."));

        if (manifest.Targets is not null)
        {
            ValidateKnownTargetKeys(manifest.Targets, "$.targets", ["client", "daemon"], diagnostics);
            if (manifest.Targets.Client is not null)
            {
                ValidateKnownTargetKeys(manifest.Targets.Client, "$.targets.client", ["ui", "theme"], diagnostics);
                if (manifest.Targets.Client.Ui is not null)
                    ValidateManifestFileRef(manifest.Targets.Client.Ui, "$.targets.client.ui", diagnostics);
                if (manifest.Targets.Client.Theme is not null)
                    ValidateManifestFileRef(manifest.Targets.Client.Theme, "$.targets.client.theme", diagnostics);
            }

            if (manifest.Targets.Daemon is not null)
            {
                ValidateKnownTargetKeys(manifest.Targets.Daemon, "$.targets.daemon", ["plugin"], diagnostics);
                if (manifest.Targets.Daemon.Plugin is not null)
                {
                    ValidateManifestFileRef(manifest.Targets.Daemon.Plugin, "$.targets.daemon.plugin", diagnostics);
                    ValidateDaemonBundleRef(manifest.Targets.Daemon.Plugin, "$.targets.daemon.plugin", diagnostics);
                }
            }
        }

        if (manifest.Entry is not null)
        {
            if (manifest.Entry.Ui is not null)
                ValidateManifestFileRef(manifest.Entry.Ui, "$.entry.ui", diagnostics);
            if (manifest.Entry.Script is not null)
            {
                ValidateManifestFileRef(manifest.Entry.Script, "$.entry.script", diagnostics);
                if (!string.IsNullOrWhiteSpace(manifest.Entry.Script.Module) && manifest.Entry.Script.Module != "esm")
                    diagnostics.Add(Diagnostic("script_module_invalid", "$.entry.script.module", "Only ESM plugin bundles are allowed."));
            }
        }

        if (uiRef is null && themeRef is null && daemonPluginRef is null && !(manifest.Targets?.Client is not null && (manifest.Resources ?? []).Count > 0))
            diagnostics.Add(Diagnostic("target_missing", "$.targets", "Extension package must declare at least one supported client or daemon target."));

        ValidateExtensionPoints(manifest.ExtensionPoints, diagnostics);
        ValidateDependencies(manifest, diagnostics);
        ValidateUpdatePolicy(manifest.Updates, diagnostics);
        ValidateCommands(manifest, daemonPluginRef, diagnostics);
        ValidatePluginExtensionEvents(manifest, daemonPluginRef, diagnostics);

        if (manifest.Integrity?.Algorithm != "sha256")
            diagnostics.Add(Diagnostic("integrity_algorithm_invalid", "$.integrity.algorithm", "Only sha256 package integrity is supported."));

        var hostCapabilities = manifest.Permissions?.Host ?? [];
        HashSet<string> seenCapabilities = [];
        for (var i = 0; i < hostCapabilities.Count; i++)
        {
            var capability = hostCapabilities[i];
            var path = $"$.permissions.host[{i}]";
            if (string.IsNullOrWhiteSpace(capability))
            {
                diagnostics.Add(Diagnostic("capability_invalid", path, "Capability must be a string."));
                continue;
            }

            if (!seenCapabilities.Add(capability))
                diagnostics.Add(Diagnostic("capability_duplicate", path, "Capability is duplicated."));
            if (!allowedHostCapabilities.Contains(capability))
                diagnostics.Add(Diagnostic("capability_unknown_or_unsupported", path, $"Capability '{capability}' is not allowed by the WPF MVP host."));
        }

        var events = manifest.Permissions?.Events ?? [];
        var packageId = manifest.Package?.Id ?? string.Empty;
        for (var i = 0; i < events.Count; i++)
        {
            if (!IsAllowedEventPermission(events[i], packageId))
                diagnostics.Add(Diagnostic("event_permission_unknown", $"$.permissions.events[{i}]", "Event permission is not in the allowlist."));
        }

        if ((manifest.Permissions?.Network ?? []).Count > 0)
            diagnostics.Add(Diagnostic("network_permissions_unsupported", "$.permissions.network", "Network access is disabled for WPF MVP plugins."));

        var privateBytes = manifest.Permissions?.Storage?.PrivateBytes;
        if (privateBytes is < 0 || privateBytes > limits.MaxPrivateStorageBytes)
            diagnostics.Add(Diagnostic("storage_quota_invalid", "$.permissions.storage.privateBytes", $"Private storage quota must be between 0 and {limits.MaxPrivateStorageBytes}."));

        if (manifest.Resources is not null)
        {
            for (var i = 0; i < manifest.Resources.Count; i++)
            {
                var resource = manifest.Resources[i];
                var path = $"$.resources[{i}]";
                ValidateManifestFileRef(resource, path, diagnostics);
                if (!AllowedResourceMimeTypes.Contains(resource.Mime ?? string.Empty))
                    diagnostics.Add(Diagnostic("resource_mime_invalid", $"{path}.mime", "Resource MIME is not allowed."));
                if (resource.Bytes is null or < 0 || resource.Bytes > limits.MaxResourceBytes)
                    diagnostics.Add(Diagnostic("resource_size_invalid", $"{path}.bytes", $"Resource bytes must be between 0 and {limits.MaxResourceBytes}."));
            }
        }
    }

    private static bool IsAllowedEventPermission(string? eventName, string packageId) =>
        eventName is not null &&
        (AllowedEventCapabilities.Contains(eventName) ||
         StringComparer.Ordinal.Equals(eventName, $"plugin.{packageId}.event.extension"));

    private sealed record ManifestFileSelection(MpxManifestFileRefDocument Ref, string Path);

    private static ManifestFileSelection? SelectClientUiRef(MpxManifestDocument manifest)
    {
        if (manifest.Targets?.Client?.Ui is not null)
            return new ManifestFileSelection(manifest.Targets.Client.Ui, "$.targets.client.ui");
        if (manifest.Entry?.Ui is not null)
            return new ManifestFileSelection(manifest.Entry.Ui, "$.entry.ui");
        return null;
    }

    private static ManifestFileSelection? SelectClientThemeRef(MpxManifestDocument manifest)
    {
        if (manifest.Targets?.Client?.Theme is not null)
            return new ManifestFileSelection(manifest.Targets.Client.Theme, "$.targets.client.theme");
        return null;
    }

    private static ManifestFileSelection? SelectScriptRef(MpxManifestDocument manifest) =>
        manifest.Entry?.Script is null
            ? null
            : new ManifestFileSelection(manifest.Entry.Script, "$.entry.script");

    private static ManifestFileSelection? SelectDaemonPluginRef(MpxManifestDocument manifest) =>
        manifest.Targets?.Daemon?.Plugin is null
            ? null
            : new ManifestFileSelection(manifest.Targets.Daemon.Plugin, "$.targets.daemon.plugin");

    private static void ValidateKnownTargetKeys(
        IManifestExtensionData document,
        string path,
        IReadOnlyCollection<string> allowed,
        List<PluginMpxDiagnostic> diagnostics)
    {
        _ = allowed;
        if (document.ExtensionData is null)
            return;

        foreach (var key in document.ExtensionData.Keys)
            diagnostics.Add(Diagnostic("target_unknown", $"{path}.{key}", $"Target property '{key}' is not supported by this package validator."));
    }

    private static PluginMpxDeploymentPlan BuildDeploymentPlan(
        MpxManifestDocument manifest,
        ManifestFileSelection? uiRef,
        ManifestFileSelection? themeRef,
        ManifestFileSelection? scriptRef,
        ManifestFileSelection? daemonPluginRef)
    {
        List<string> scopes = [];
        PluginMpxClientDeployment? client = null;
        PluginMpxDaemonDeployment? daemon = null;

        if (uiRef is not null || themeRef is not null || scriptRef is not null || (manifest.Targets?.Client is not null && (manifest.Resources ?? []).Count > 0))
        {
            client = new PluginMpxClientDeployment(
                ToDeploymentFile(uiRef?.Ref),
                ToDeploymentFile(themeRef?.Ref),
                ToDeploymentScript(scriptRef?.Ref as MpxManifestScriptRefDocument),
                new ReadOnlyCollection<PluginMpxDeploymentResource>((manifest.Resources ?? []).Select(ToDeploymentResource).ToArray()));
            scopes.Add("client");
        }

        if (daemonPluginRef is not null)
        {
            daemon = new PluginMpxDaemonDeployment(
                ToDeploymentFile(daemonPluginRef.Ref),
                new ReadOnlyCollection<PluginMpxExtensionPoint>((manifest.ExtensionPoints ?? []).Select(ToExtensionPoint).ToArray()),
                new ReadOnlyCollection<PluginMpxCommand>((manifest.Commands ?? []).Select(ToCommand).ToArray()));
            scopes.Add("daemon");
        }

        return new PluginMpxDeploymentPlan(new ReadOnlyCollection<string>(scopes), client, daemon);
    }

    private static PluginMpxDeploymentFile? ToDeploymentFile(MpxManifestFileRefDocument? document) =>
        document is null ? null : new PluginMpxDeploymentFile(document.Path!, document.Sha256!);

    private static PluginMpxDeploymentScript? ToDeploymentScript(MpxManifestScriptRefDocument? document) =>
        document is null ? null : new PluginMpxDeploymentScript(document.Path!, document.Sha256!, document.Module);

    private static PluginMpxDeploymentResource ToDeploymentResource(MpxManifestResourceDocument document) =>
        new(document.Path!, document.Sha256!, document.Mime!, document.Bytes!.Value);

    private static PluginMpxExtensionPoint ToExtensionPoint(MpxManifestExtensionPointDocument document) =>
        new(document.Kind!, document.Id!, document.Target);

    private static PluginMpxCommand ToCommand(MpxManifestCommandDocument document) =>
        new(document.Id!, document.Title, document.Description, document.Target);

    private static PluginMpxExtensionDependency ToDependency(MpxManifestExtensionDependencyDocument document) =>
        new(document.Id!, document.Version!);

    private static PluginMpxUpdatePolicy? ToUpdatePolicy(MpxManifestUpdatePolicyDocument? document) =>
        document is null ? null : new PluginMpxUpdatePolicy(document.Channel ?? "stable", document.Strategy ?? "manual");

    private static PluginMpxSignatureTrust? ValidatePackageSignature(
        IReadOnlyDictionary<string, byte[]> entries,
        MpxManifestDocument manifest,
        IReadOnlyList<PluginMpxTrustedPublisher>? trustedPublishers,
        List<PluginMpxDiagnostic> diagnostics)
    {
        if (manifest.Integrity?.Signed is not true)
            return null;

        if (!entries.TryGetValue(SignatureManifestPath, out var signatureManifestBytes))
        {
            diagnostics.Add(Diagnostic("signature_manifest_missing", SignatureManifestPath, "Signed packages must include signatures/manifest.json."));
            return null;
        }

        foreach (var path in entries.Keys)
        {
            if (path.StartsWith(SignatureDirectory, StringComparison.Ordinal) && path != SignatureManifestPath)
                diagnostics.Add(Diagnostic("signature_unexpected", path, "Only signatures/manifest.json is supported for signed .mpx packages."));
        }

        var document = ParseSignatureManifest(signatureManifestBytes, diagnostics);
        if (document is null)
            return null;

        if (document.Algorithm != SignatureAlgorithm)
            diagnostics.Add(Diagnostic("signature_algorithm_unsupported", "$.algorithm", $"Only {SignatureAlgorithm} signatures are supported."));
        if (document.Payload?.Algorithm != SignaturePayloadAlgorithm)
            diagnostics.Add(Diagnostic("signature_payload_algorithm_invalid", "$.payload.algorithm", "Only sha256 payload digests are supported."));

        var package = manifest.Package;
        if (string.IsNullOrWhiteSpace(package?.Publisher))
            diagnostics.Add(Diagnostic("signature_publisher_missing", "$.package.publisher", "Signed packages must declare package.publisher."));
        if (!StringComparer.Ordinal.Equals(document.Publisher, package?.Publisher))
            diagnostics.Add(Diagnostic("signature_publisher_mismatch", "$.publisher", "Signature publisher must match package.publisher."));
        if (!StringComparer.Ordinal.Equals(document.PackageId, package?.Id))
            diagnostics.Add(Diagnostic("signature_package_mismatch", "$.packageId", "Signature packageId must match package.id."));
        if (!StringComparer.Ordinal.Equals(document.PackageVersion, package?.Version))
            diagnostics.Add(Diagnostic("signature_version_mismatch", "$.packageVersion", "Signature packageVersion must match package.version."));
        if (string.IsNullOrWhiteSpace(document.KeyId))
            diagnostics.Add(Diagnostic("signature_key_missing", "$.keyId", "Signed packages must declare keyId."));
        if (string.IsNullOrWhiteSpace(document.PublicKeySha256) || !Sha256Regex().IsMatch(document.PublicKeySha256))
            diagnostics.Add(Diagnostic("signature_key_fingerprint_invalid", "$.publicKeySha256", "publicKeySha256 must be a lowercase SHA-256 fingerprint."));
        if (string.IsNullOrWhiteSpace(document.Signature))
            diagnostics.Add(Diagnostic("signature_missing", "$.signature", "Signed packages must include a signature value."));

        var actualEntries = BuildSignatureEntries(entries);
        if (!SignatureEntriesMatch(document.Payload?.Entries, actualEntries))
            diagnostics.Add(Diagnostic("signature_payload_mismatch", "$.payload.entries", "Signature payload entries must exactly match package contents."));

        if (diagnostics.Count > 0)
            return null;

        if (trustedPublishers is null || trustedPublishers.Count == 0)
        {
            diagnostics.Add(Diagnostic("signature_trust_unavailable", "$.publisher", "Signed .mpx packages require a trusted publisher key before admission."));
            return null;
        }

        var trusted = trustedPublishers.FirstOrDefault(candidate =>
            StringComparer.Ordinal.Equals(candidate.Publisher, document.Publisher) &&
            StringComparer.Ordinal.Equals(candidate.KeyId, document.KeyId) &&
            StringComparer.Ordinal.Equals(Sha256Hex(candidate.PublicKeySubjectPublicKeyInfo), document.PublicKeySha256));
        if (trusted is null)
        {
            diagnostics.Add(Diagnostic("signature_key_untrusted", "$.publicKeySha256", "The package signing key is not trusted for this publisher."));
            return null;
        }

        byte[] signatureBytes;
        try
        {
            signatureBytes = Convert.FromBase64String(document.Signature!);
        }
        catch (FormatException)
        {
            diagnostics.Add(Diagnostic("signature_invalid", "$.signature", "Signature must be base64-encoded."));
            return null;
        }

        var input = BuildSignatureInput(document, actualEntries);
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(trusted.PublicKeySubjectPublicKeyInfo, out var bytesRead);
            if (bytesRead != trusted.PublicKeySubjectPublicKeyInfo.Length)
            {
                diagnostics.Add(Diagnostic("signature_key_invalid", "$.publicKeySha256", "Trusted public key contains trailing data."));
                return null;
            }

            if (!key.VerifyData(input, signatureBytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            {
                diagnostics.Add(Diagnostic("signature_invalid", "$.signature", "Package signature verification failed."));
                return null;
            }
        }
        catch (CryptographicException exception)
        {
            diagnostics.Add(Diagnostic("signature_invalid", "$.signature", exception.Message));
            return null;
        }

        DateTimeOffset? signedAt = null;
        if (!string.IsNullOrWhiteSpace(document.SignedAt))
        {
            if (DateTimeOffset.TryParse(document.SignedAt, out var parsed))
                signedAt = parsed;
            else
                diagnostics.Add(Diagnostic("signature_signed_at_invalid", "$.signedAt", "signedAt must be an ISO 8601 timestamp when present."));
        }

        return diagnostics.Count == 0
            ? new PluginMpxSignatureTrust(document.Publisher!, document.KeyId!, document.PublicKeySha256!, signedAt)
            : null;
    }

    private static MpxSignatureManifestDocument? ParseSignatureManifest(byte[] bytes, List<PluginMpxDiagnostic> diagnostics)
    {
        try
        {
            return JsonSerializer.Deserialize<MpxSignatureManifestDocument>(bytes, ManifestJsonOptions);
        }
        catch (JsonException ex)
        {
            diagnostics.Add(Diagnostic("signature_manifest_parse_failed", SignatureManifestPath, ex.Message));
            return null;
        }
    }

    private static IReadOnlyList<MpxSignatureEntryDocument> BuildSignatureEntries(IReadOnlyDictionary<string, byte[]> entries) =>
        entries
            .Where(static entry => !entry.Key.StartsWith(SignatureDirectory, StringComparison.Ordinal))
            .OrderBy(static entry => entry.Key, StringComparer.Ordinal)
            .Select(static entry => new MpxSignatureEntryDocument
            {
                Path = entry.Key,
                Sha256 = Sha256Hex(entry.Value)
            })
            .ToArray();

    private static bool SignatureEntriesMatch(
        List<MpxSignatureEntryDocument>? declared,
        IReadOnlyList<MpxSignatureEntryDocument> actual)
    {
        if (declared is null || declared.Count != actual.Count)
            return false;

        var ordered = declared.OrderBy(static entry => entry.Path, StringComparer.Ordinal).ToArray();
        for (var i = 0; i < actual.Count; i++)
        {
            if (!StringComparer.Ordinal.Equals(ordered[i].Path, actual[i].Path) ||
                !StringComparer.Ordinal.Equals(ordered[i].Sha256, actual[i].Sha256))
            {
                return false;
            }
        }

        return true;
    }

    private static byte[] BuildSignatureInput(
        MpxSignatureManifestDocument document,
        IReadOnlyList<MpxSignatureEntryDocument> entries)
    {
        var builder = new StringBuilder();
        builder.Append(SignatureInputHeader).Append('\n');
        AppendSignatureField(builder, "algorithm", document.Algorithm);
        AppendSignatureField(builder, "publisher", document.Publisher);
        AppendSignatureField(builder, "packageId", document.PackageId);
        AppendSignatureField(builder, "packageVersion", document.PackageVersion);
        AppendSignatureField(builder, "keyId", document.KeyId);
        AppendSignatureField(builder, "publicKeySha256", document.PublicKeySha256);
        AppendSignatureField(builder, "signedAt", document.SignedAt);
        AppendSignatureField(builder, "payloadAlgorithm", document.Payload?.Algorithm);
        foreach (var entry in entries)
            builder.Append("entry:").Append(entry.Path).Append(':').Append(entry.Sha256).Append('\n');
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static void AppendSignatureField(StringBuilder builder, string key, string? value) =>
        builder.Append(key).Append(':').Append(value ?? string.Empty).Append('\n');

    private static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void RejectUnexpectedSignatureEntries(
        IEnumerable<string> paths,
        MpxManifestDocument manifest,
        List<PluginMpxDiagnostic> diagnostics)
    {
        if (manifest.Integrity?.Signed == true)
            return;

        foreach (var path in paths)
        {
            if (path.StartsWith(SignatureDirectory, StringComparison.Ordinal))
                diagnostics.Add(Diagnostic("signature_unexpected", path, "Signature payloads are only accepted for signed packages."));
        }
    }

    private static void ValidateExtensionPoints(List<MpxManifestExtensionPointDocument>? extensionPoints, List<PluginMpxDiagnostic> diagnostics)
    {
        if (extensionPoints is null)
            return;

        HashSet<string> seen = [];
        for (var i = 0; i < extensionPoints.Count; i++)
        {
            var extensionPoint = extensionPoints[i];
            var path = $"$.extensionPoints[{i}]";
            if (string.IsNullOrWhiteSpace(extensionPoint.Kind) || !AllowedExtensionPointKinds.Contains(extensionPoint.Kind))
                diagnostics.Add(Diagnostic("extension_point_kind_unknown", $"{path}.kind", "Extension point kind is not supported."));
            if (string.IsNullOrWhiteSpace(extensionPoint.Id) || !AllowedExtensionPointIds.Contains(extensionPoint.Id))
            {
                diagnostics.Add(Diagnostic("extension_point_unknown", $"{path}.id", "Extension point id is not supported by this package validator."));
            }
            else if (!seen.Add(extensionPoint.Id))
            {
                diagnostics.Add(Diagnostic("extension_point_duplicate", $"{path}.id", "Extension point id is duplicated."));
            }

            if (!string.IsNullOrWhiteSpace(extensionPoint.Target) && extensionPoint.Target is not "client" and not "daemon")
                diagnostics.Add(Diagnostic("extension_point_target_unknown", $"{path}.target", "Extension point target must be client or daemon."));
        }
    }

    private static void ValidateDependencies(MpxManifestDocument manifest, List<PluginMpxDiagnostic> diagnostics)
    {
        var dependencies = manifest.Dependencies?.Extensions;
        if (dependencies is null)
            return;

        HashSet<string> seen = [];
        for (var i = 0; i < dependencies.Count; i++)
        {
            var dependency = dependencies[i];
            var path = $"$.dependencies.extensions[{i}]";
            if (string.IsNullOrWhiteSpace(dependency.Id) || !PackageIdRegex().IsMatch(dependency.Id))
            {
                diagnostics.Add(Diagnostic("dependency_id_invalid", $"{path}.id", "Extension dependency id is invalid."));
            }
            else
            {
                if (StringComparer.Ordinal.Equals(dependency.Id, manifest.Package?.Id))
                    diagnostics.Add(Diagnostic("dependency_self", $"{path}.id", "Extension packages must not depend on themselves."));
                if (!seen.Add(dependency.Id))
                    diagnostics.Add(Diagnostic("dependency_duplicate", $"{path}.id", "Extension dependency is duplicated."));
            }

            if (string.IsNullOrWhiteSpace(dependency.Version) || !IsSupportedVersionRange(dependency.Version))
                diagnostics.Add(Diagnostic("dependency_version_invalid", $"{path}.version", "Extension dependency version must be exact semver or a bracketed semver range."));
        }
    }

    private static void ValidateUpdatePolicy(MpxManifestUpdatePolicyDocument? updates, List<PluginMpxDiagnostic> diagnostics)
    {
        if (updates is null)
            return;

        if (!string.IsNullOrWhiteSpace(updates.Channel) && !AllowedUpdateChannels.Contains(updates.Channel))
            diagnostics.Add(Diagnostic("update_channel_invalid", "$.updates.channel", "Update channel must be stable or preview."));
        if (!string.IsNullOrWhiteSpace(updates.Strategy) && !AllowedUpdateStrategies.Contains(updates.Strategy))
            diagnostics.Add(Diagnostic("update_strategy_invalid", "$.updates.strategy", "Only manual update strategy is implemented."));
    }

    internal static bool IsDependencyVersionSatisfied(string requiredRange, string installedVersion)
    {
        if (string.IsNullOrWhiteSpace(requiredRange) || string.IsNullOrWhiteSpace(installedVersion))
            return false;
        if (SemVerLikeRegex().IsMatch(requiredRange))
            return StringComparer.Ordinal.Equals(requiredRange, installedVersion);
        var match = VersionRangeRegex().Match(requiredRange);
        if (!match.Success || !TryParseSemVerCore(installedVersion, out var installed))
            return false;

        var minText = match.Groups[1].Value;
        var maxText = match.Groups[2].Value;
        var minInclusive = requiredRange[0] == '[';
        var maxInclusive = requiredRange[^1] == ']';
        if (!string.IsNullOrEmpty(minText) && TryParseSemVerCore(minText, out var min))
        {
            var compare = CompareSemVerCore(installed, min);
            if (compare < 0 || (compare == 0 && !minInclusive))
                return false;
        }
        if (!string.IsNullOrEmpty(maxText) && TryParseSemVerCore(maxText, out var max))
        {
            var compare = CompareSemVerCore(installed, max);
            if (compare > 0 || (compare == 0 && !maxInclusive))
                return false;
        }

        return true;
    }

    private static bool IsSupportedVersionRange(string value) =>
        SemVerLikeRegex().IsMatch(value) || VersionRangeRegex().IsMatch(value);

    private static bool TryParseSemVerCore(string value, out (int Major, int Minor, int Patch) version)
    {
        version = default;
        var core = value.Split(['-', '+'], 2)[0];
        var parts = core.Split('.');
        if (parts.Length != 3 ||
            !int.TryParse(parts[0], out var major) ||
            !int.TryParse(parts[1], out var minor) ||
            !int.TryParse(parts[2], out var patch))
        {
            return false;
        }

        version = (major, minor, patch);
        return true;
    }

    private static int CompareSemVerCore((int Major, int Minor, int Patch) left, (int Major, int Minor, int Patch) right)
    {
        var major = left.Major.CompareTo(right.Major);
        if (major != 0)
            return major;
        var minor = left.Minor.CompareTo(right.Minor);
        return minor != 0 ? minor : left.Patch.CompareTo(right.Patch);
    }

    private static void ValidatePluginExtensionEvents(MpxManifestDocument manifest, ManifestFileSelection? daemonPluginRef, List<PluginMpxDiagnostic> diagnostics)
    {
        var eventName = $"plugin.{manifest.Package?.Id}.event.extension";
        if (!(manifest.Permissions?.Events ?? []).Contains(eventName, StringComparer.Ordinal))
            return;

        if (daemonPluginRef is null)
            diagnostics.Add(Diagnostic("event_daemon_target_missing", "$.targets.daemon.plugin", "Plugin-owned extension events require a daemon plugin payload."));
        if (!HasDaemonEventExtensionPoint(manifest.ExtensionPoints))
            diagnostics.Add(Diagnostic("event_extension_point_missing", "$.extensionPoints", "Plugin-owned extension events require the event.daemon extension point."));
    }

    private static bool HasDaemonEventExtensionPoint(List<MpxManifestExtensionPointDocument>? extensionPoints) =>
        (extensionPoints ?? []).Any(extensionPoint =>
            StringComparer.Ordinal.Equals(extensionPoint.Kind, "event") &&
            StringComparer.Ordinal.Equals(extensionPoint.Id, "event.daemon") &&
            (extensionPoint.Target is null || StringComparer.Ordinal.Equals(extensionPoint.Target, "daemon")));

    private static void ValidateCommands(MpxManifestDocument manifest, ManifestFileSelection? daemonPluginRef, List<PluginMpxDiagnostic> diagnostics)
    {
        if (manifest.Commands is null)
            return;

        HashSet<string> seen = [];
        for (var i = 0; i < manifest.Commands.Count; i++)
        {
            var command = manifest.Commands[i];
            var path = $"$.commands[{i}]";
            if (string.IsNullOrWhiteSpace(command.Id) || !CommandIdRegex().IsMatch(command.Id))
            {
                diagnostics.Add(Diagnostic("command_id_invalid", $"{path}.id", "Command id must be canonical."));
            }
            else if (!seen.Add(command.Id))
            {
                diagnostics.Add(Diagnostic("command_duplicate", $"{path}.id", "Command id is duplicated."));
            }

            if (command.Title is not null && string.IsNullOrWhiteSpace(command.Title))
                diagnostics.Add(Diagnostic("command_title_invalid", $"{path}.title", "Command title must be a non-empty string when present."));
            if (command.Description is not null && string.IsNullOrWhiteSpace(command.Description))
                diagnostics.Add(Diagnostic("command_description_invalid", $"{path}.description", "Command description must be a non-empty string when present."));
            if (!string.IsNullOrWhiteSpace(command.Target) && command.Target != "daemon")
                diagnostics.Add(Diagnostic("command_target_invalid", $"{path}.target", "Command target must be daemon."));
        }

        if (manifest.Commands.Count == 0)
            return;
        if (daemonPluginRef is null)
            diagnostics.Add(Diagnostic("command_daemon_target_missing", "$.targets.daemon.plugin", "Daemon-backed commands require a daemon plugin payload."));
        if (!HasDaemonCommandExtensionPoint(manifest.ExtensionPoints))
            diagnostics.Add(Diagnostic("command_extension_point_missing", "$.extensionPoints", "Daemon-backed commands require the command.daemon extension point."));
    }

    private static bool HasDaemonCommandExtensionPoint(List<MpxManifestExtensionPointDocument>? extensionPoints) =>
        (extensionPoints ?? []).Any(extensionPoint =>
            extensionPoint.Kind == "command" &&
            extensionPoint.Id == "command.daemon" &&
            (string.IsNullOrWhiteSpace(extensionPoint.Target) || extensionPoint.Target == "daemon"));

    private static void ValidateUiCommandBindings(PluginUiSchema? schema, List<MpxManifestCommandDocument>? commands, List<PluginMpxDiagnostic> diagnostics)
    {
        if (schema is null)
            return;

        HashSet<string> declared = new(StringComparer.Ordinal);
        foreach (var command in commands ?? [])
        {
            if (!string.IsNullOrWhiteSpace(command.Id))
                declared.Add(command.Id);
        }
        VisitUiNode(schema.Root, "$.targets.client.ui:$.root", (path, command) =>
        {
            if (!declared.Contains(command))
                diagnostics.Add(Diagnostic("command_undeclared", path, $"UI command '{command}' is not declared in manifest.commands."));
        });
    }

    private static void VisitUiNode(PluginUiNode node, string path, Action<string, string> onCommand)
    {
        CollectCommandFromProp(GetProp(node, "OnClick"), $"{path}.props.OnClick", onCommand);
        CollectCommandFromProp(GetProp(node, "OnChanged"), $"{path}.props.OnChanged", onCommand);
        for (var i = 0; i < node.Children.Count; i++)
            VisitUiNode(node.Children[i], $"{path}.children[{i}]", onCommand);
        if (GetProp(node, "Items") is IReadOnlyList<PluginUiTabItem> items)
        {
            for (var i = 0; i < items.Count; i++)
            {
                for (var childIndex = 0; childIndex < items[i].Children.Count; childIndex++)
                    VisitUiNode(items[i].Children[childIndex], $"{path}.props.Items[{i}].Children[{childIndex}]", onCommand);
            }
        }
    }

    private static object? GetProp(PluginUiNode node, string key) =>
        node.Props.TryGetValue(key, out var value) ? value : null;

    private static void CollectCommandFromProp(object? value, string path, Action<string, string> onCommand)
    {
        if (value is IReadOnlyDictionary<string, object?> command &&
            command.TryGetValue("Command", out var commandValue) &&
            commandValue is string commandText)
        {
            onCommand($"{path}.Command", commandText);
        }
    }

    private static void ValidateDaemonBundleRef(MpxManifestFileRefDocument? fileRef, string path, List<PluginMpxDiagnostic> diagnostics)
    {
        if (fileRef?.Path is null)
            return;
        if (!TryNormalizePath(fileRef.Path, DefaultLimits, out var normalizedPath, out _))
            return;
        if (!normalizedPath.EndsWith(".zip", StringComparison.Ordinal))
        {
            diagnostics.Add(Diagnostic(
                "daemon_bundle_path_invalid",
                $"{path}.path",
                "Daemon plugin payload must be a zip bundle containing mcsl-plugin.json and plugin assemblies."));
        }
    }

    private static void ValidateManifestFileRef(MpxManifestFileRefDocument? fileRef, string path, List<PluginMpxDiagnostic> diagnostics)
    {
        if (fileRef is null)
        {
            diagnostics.Add(Diagnostic("file_ref_missing", path, "File reference is required."));
            return;
        }

        if (string.IsNullOrWhiteSpace(fileRef.Path))
        {
            diagnostics.Add(Diagnostic("path_invalid", $"{path}.path", "Package path is invalid."));
        }
        else if (!TryNormalizePath(fileRef.Path, DefaultLimits, out _, out var pathDiagnostic))
        {
            diagnostics.Add(pathDiagnostic with { Path = $"{path}.path" });
        }
        if (string.IsNullOrWhiteSpace(fileRef.Sha256) || !Sha256Regex().IsMatch(fileRef.Sha256))
            diagnostics.Add(Diagnostic("sha256_invalid", $"{path}.sha256", "sha256 must be a lowercase 64-character hex digest."));
    }

    private static HashSet<string> CollectDeclaredFiles(MpxManifestDocument manifest, List<PluginMpxDiagnostic> diagnostics)
    {
        HashSet<string> declared = new(StringComparer.Ordinal);
        AddDeclaredFile(declared, manifest.Targets?.Client?.Ui?.Path, "$.targets.client.ui.path", diagnostics);
        AddDeclaredFile(declared, manifest.Targets?.Client?.Theme?.Path, "$.targets.client.theme.path", diagnostics);
        AddDeclaredFile(declared, manifest.Targets?.Daemon?.Plugin?.Path, "$.targets.daemon.plugin.path", diagnostics);
        AddDeclaredFile(declared, manifest.Entry?.Ui?.Path, "$.entry.ui.path", diagnostics);
        AddDeclaredFile(declared, manifest.Entry?.Script?.Path, "$.entry.script.path", diagnostics);

        if (manifest.Resources is not null)
        {
            for (var i = 0; i < manifest.Resources.Count; i++)
                AddDeclaredFile(declared, manifest.Resources[i].Path!, $"$.resources[{i}].path", diagnostics);
        }

        return declared;
    }

    private static void AddDeclaredFile(HashSet<string> declared, string? path, string diagnosticPath, List<PluginMpxDiagnostic> diagnostics)
    {
        if (path is null)
            return;

        if (!declared.Add(path))
            diagnostics.Add(Diagnostic("file_ref_duplicate", diagnosticPath, "File reference path is duplicated."));
    }

    private static void ValidatePayloadFile(
        Dictionary<string, byte[]> entries,
        MpxManifestFileRefDocument fileRef,
        string path,
        int maxBytes,
        Dictionary<string, string> fileDigests,
        List<PluginMpxDiagnostic> diagnostics)
    {
        if (!entries.TryGetValue(fileRef.Path!, out var bytes))
        {
            diagnostics.Add(Diagnostic("payload_missing", path, $"Payload '{fileRef.Path}' is missing."));
            return;
        }

        if (bytes.Length > maxBytes)
        {
            diagnostics.Add(Diagnostic("payload_too_large", path, $"Payload exceeds {maxBytes} bytes."));
            return;
        }

        var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        fileDigests[fileRef.Path!] = actual;
        if (!string.Equals(actual, fileRef.Sha256, StringComparison.Ordinal))
            diagnostics.Add(Diagnostic("sha256_mismatch", $"{path}.sha256", $"Payload '{fileRef.Path}' does not match its manifest digest."));
    }

    private static bool TryNormalizePath(
        string path,
        PluginMpxPackageLimits limits,
        out string normalized,
        out PluginMpxDiagnostic diagnostic)
    {
        normalized = path;
        diagnostic = Diagnostic("path_invalid", path, "Package path is invalid.");

        if (string.IsNullOrWhiteSpace(path))
            return false;
        if (path.Length > limits.MaxPathLength)
        {
            diagnostic = Diagnostic("path_too_long", path, $"Package path exceeds {limits.MaxPathLength} characters.");
            return false;
        }
        if (path.Contains("\\", StringComparison.Ordinal) || path.Any(char.IsControl))
            return false;
        if (path.StartsWith("/", StringComparison.Ordinal) || DrivePathRegex().IsMatch(path))
        {
            diagnostic = Diagnostic("path_absolute", path, "Absolute package paths are not allowed.");
            return false;
        }

        var segments = path.Split('/');
        if (segments.Any(segment => segment is "" or "." or ".."))
        {
            diagnostic = Diagnostic("path_traversal", path, "Package paths must not traverse directories.");
            return false;
        }

        return true;
    }

    private static PluginMpxDiagnostic Diagnostic(string code, string path, string message) =>
        new(code, path, message);

    private static IReadOnlySet<string> Set(params string[] values) =>
        new HashSet<string>(values, StringComparer.Ordinal);

    [GeneratedRegex(@"^[a-z0-9][a-z0-9.-]{2,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex PackageIdRegex();

    [GeneratedRegex(@"^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex SemVerLikeRegex();

    [GeneratedRegex(@"^[\[(](\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?)?,(\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?)?[\])]$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionRangeRegex();

    [GeneratedRegex(@"^[a-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Regex();

    [GeneratedRegex(@"^[A-Za-z_][\w]*(?:\.[A-Za-z_][\w]*)*$", RegexOptions.CultureInvariant)]
    private static partial Regex CommandIdRegex();

    [GeneratedRegex(@"^[A-Za-z]:", RegexOptions.CultureInvariant)]
    private static partial Regex DrivePathRegex();

    private interface IManifestExtensionData
    {
        Dictionary<string, JsonElement>? ExtensionData { get; }
    }

    private sealed class MpxSignatureManifestDocument
    {
        [JsonPropertyName("schema")]
        public string? Schema { get; init; }

        [JsonPropertyName("algorithm")]
        public string? Algorithm { get; init; }

        [JsonPropertyName("publisher")]
        public string? Publisher { get; init; }

        [JsonPropertyName("packageId")]
        public string? PackageId { get; init; }

        [JsonPropertyName("packageVersion")]
        public string? PackageVersion { get; init; }

        [JsonPropertyName("keyId")]
        public string? KeyId { get; init; }

        [JsonPropertyName("publicKeySha256")]
        public string? PublicKeySha256 { get; init; }

        [JsonPropertyName("signedAt")]
        public string? SignedAt { get; init; }

        [JsonPropertyName("payload")]
        public MpxSignaturePayloadDocument? Payload { get; init; }

        [JsonPropertyName("signature")]
        public string? Signature { get; init; }
    }

    private sealed class MpxSignaturePayloadDocument
    {
        [JsonPropertyName("algorithm")]
        public string? Algorithm { get; init; }

        [JsonPropertyName("entries")]
        public List<MpxSignatureEntryDocument>? Entries { get; init; }
    }

    private sealed class MpxSignatureEntryDocument
    {
        [JsonPropertyName("path")]
        public string? Path { get; init; }

        [JsonPropertyName("sha256")]
        public string? Sha256 { get; init; }
    }

    private sealed class MpxManifestDocument
    {
        [JsonPropertyName("schema")]
        public string? Schema { get; init; }

        [JsonPropertyName("package")]
        public MpxManifestPackageDocument? Package { get; init; }

        [JsonPropertyName("runtime")]
        public MpxManifestRuntimeDocument? Runtime { get; init; }

        [JsonPropertyName("targets")]
        public MpxManifestTargetsDocument? Targets { get; init; }

        [JsonPropertyName("entry")]
        public MpxManifestEntryDocument? Entry { get; init; }

        [JsonPropertyName("permissions")]
        public MpxManifestPermissionsDocument? Permissions { get; init; }

        [JsonPropertyName("resources")]
        public List<MpxManifestResourceDocument>? Resources { get; init; }

        [JsonPropertyName("extensionPoints")]
        public List<MpxManifestExtensionPointDocument>? ExtensionPoints { get; init; }

        [JsonPropertyName("commands")]
        public List<MpxManifestCommandDocument>? Commands { get; init; }

        [JsonPropertyName("dependencies")]
        public MpxManifestDependenciesDocument? Dependencies { get; init; }

        [JsonPropertyName("updates")]
        public MpxManifestUpdatePolicyDocument? Updates { get; init; }

        [JsonPropertyName("integrity")]
        public MpxManifestIntegrityDocument? Integrity { get; init; }
    }

    private sealed class MpxManifestPackageDocument
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("version")]
        public string? Version { get; init; }

        [JsonPropertyName("publisher")]
        public string? Publisher { get; init; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; init; }
    }

    private sealed class MpxManifestRuntimeDocument
    {
        [JsonPropertyName("ui")]
        public string? Ui { get; init; }
    }

    private sealed class MpxManifestTargetsDocument : IManifestExtensionData
    {
        [JsonPropertyName("client")]
        public MpxManifestClientTargetDocument? Client { get; init; }

        [JsonPropertyName("daemon")]
        public MpxManifestDaemonTargetDocument? Daemon { get; init; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; init; }
    }

    private sealed class MpxManifestClientTargetDocument : IManifestExtensionData
    {
        [JsonPropertyName("ui")]
        public MpxManifestFileRefDocument? Ui { get; init; }

        [JsonPropertyName("theme")]
        public MpxManifestFileRefDocument? Theme { get; init; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; init; }
    }

    private sealed class MpxManifestDaemonTargetDocument : IManifestExtensionData
    {
        [JsonPropertyName("plugin")]
        public MpxManifestFileRefDocument? Plugin { get; init; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; init; }
    }

    private sealed class MpxManifestEntryDocument
    {
        [JsonPropertyName("ui")]
        public MpxManifestFileRefDocument? Ui { get; init; }

        [JsonPropertyName("script")]
        public MpxManifestScriptRefDocument? Script { get; init; }
    }

    private class MpxManifestFileRefDocument
    {
        [JsonPropertyName("path")]
        public string? Path { get; init; }

        [JsonPropertyName("sha256")]
        public string? Sha256 { get; init; }
    }

    private sealed class MpxManifestScriptRefDocument : MpxManifestFileRefDocument
    {
        [JsonPropertyName("module")]
        public string? Module { get; init; }
    }

    private sealed class MpxManifestResourceDocument : MpxManifestFileRefDocument
    {
        [JsonPropertyName("mime")]
        public string? Mime { get; init; }

        [JsonPropertyName("bytes")]
        public int? Bytes { get; init; }
    }

    private sealed class MpxManifestExtensionPointDocument
    {
        [JsonPropertyName("kind")]
        public string? Kind { get; init; }

        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("target")]
        public string? Target { get; init; }
    }

    private sealed class MpxManifestCommandDocument
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("title")]
        public string? Title { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonPropertyName("target")]
        public string? Target { get; init; }
    }

    private sealed class MpxManifestDependenciesDocument
    {
        [JsonPropertyName("extensions")]
        public List<MpxManifestExtensionDependencyDocument>? Extensions { get; init; }
    }

    private sealed class MpxManifestExtensionDependencyDocument
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("version")]
        public string? Version { get; init; }
    }

    private sealed class MpxManifestUpdatePolicyDocument
    {
        [JsonPropertyName("channel")]
        public string? Channel { get; init; }

        [JsonPropertyName("strategy")]
        public string? Strategy { get; init; }
    }

    private sealed class MpxManifestPermissionsDocument
    {
        [JsonPropertyName("host")]
        public List<string>? Host { get; init; }

        [JsonPropertyName("events")]
        public List<string>? Events { get; init; }

        [JsonPropertyName("network")]
        public List<string>? Network { get; init; }

        [JsonPropertyName("storage")]
        public MpxManifestStorageDocument? Storage { get; init; }
    }

    private sealed class MpxManifestStorageDocument
    {
        [JsonPropertyName("privateBytes")]
        public int? PrivateBytes { get; init; }
    }

    private sealed class MpxManifestIntegrityDocument
    {
        [JsonPropertyName("algorithm")]
        public string? Algorithm { get; init; }

        [JsonPropertyName("signed")]
        public bool? Signed { get; init; }
    }
}
