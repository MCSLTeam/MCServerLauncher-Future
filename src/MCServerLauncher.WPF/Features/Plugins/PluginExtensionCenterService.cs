using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace MCServerLauncher.WPF.Features.Plugins;

public sealed record PluginExtensionCenterPackageReview(
    string PackagePath,
    byte[] Bytes,
    ValidatedPluginMpxPackage Package,
    IReadOnlyList<PluginClientExtensionManifestDrift> Drift);

public sealed record PluginExtensionCenterPackageReviewResult(
    bool Ok,
    PluginExtensionCenterPackageReview? Review,
    IReadOnlyList<PluginMpxDiagnostic> Diagnostics,
    string? Code,
    string? Message)
{
    public static PluginExtensionCenterPackageReviewResult Success(PluginExtensionCenterPackageReview review) =>
        new(true, review, [], null, null);

    public static PluginExtensionCenterPackageReviewResult ValidationFailed(IReadOnlyList<PluginMpxDiagnostic> diagnostics) =>
        new(false, null, diagnostics, "package_invalid", "Extension package validation failed.");

    public static PluginExtensionCenterPackageReviewResult Failure(string code, string message) =>
        new(false, null, [], code, message);
}

public sealed record PluginExtensionCenterInstallResult(
    bool Ok,
    PluginClientExtensionCacheEntry? Entry,
    IReadOnlyList<PluginMpxDiagnostic> Diagnostics,
    string? Code,
    string? Message)
{
    public static PluginExtensionCenterInstallResult Success(PluginClientExtensionCacheEntry entry) =>
        new(true, entry, [], null, null);

    public static PluginExtensionCenterInstallResult ValidationFailed(IReadOnlyList<PluginMpxDiagnostic> diagnostics) =>
        new(false, null, diagnostics, "package_invalid", "Extension package validation failed.");

    public static PluginExtensionCenterInstallResult Failure(string code, string message) =>
        new(false, null, [], code, message);
}

public sealed class PluginExtensionCenterService
{
    private readonly PluginClientExtensionManager _manager;
    private readonly PluginTrustRootStore _trustRoots;

    public PluginExtensionCenterService(string? cacheRoot = null, PluginTrustRootStore? trustRoots = null)
    {
        var root = cacheRoot ?? DefaultCacheRoot;
        _manager = new PluginClientExtensionManager(new PluginClientExtensionCacheStore(root));
        _trustRoots = trustRoots ?? new PluginTrustRootStore();
    }

    public PluginTrustRootStore TrustRoots => _trustRoots;

    public static string DefaultCacheRoot =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Plugins", "ClientExtensions");

    public IReadOnlyList<PluginClientExtensionCacheEntry> Restore()
    {
        _manager.Restore();
        return _manager.List();
    }

    public IReadOnlyList<PluginClientExtensionCacheEntry> List() => _manager.List();

    public PluginExtensionCenterPackageReviewResult ReviewPackage(string packagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(packagePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return PluginExtensionCenterPackageReviewResult.Failure("package_read_failed", exception.Message);
        }

        return ReviewPackageBytes(bytes, packagePath);
    }

    /// <summary>Review an in-memory package (e.g. downloaded from the registry).</summary>
    public PluginExtensionCenterPackageReviewResult ReviewPackageBytes(byte[] bytes, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var validation = PluginMpxPackageValidator.Validate(bytes, new PluginMpxPackageValidatorOptions
        {
            TrustedPublishers = _trustRoots.ToValidatorOptions(),
        });
        if (!validation.Ok || validation.Package is null)
            return PluginExtensionCenterPackageReviewResult.ValidationFailed(validation.Diagnostics);

        return PluginExtensionCenterPackageReviewResult.Success(
            new PluginExtensionCenterPackageReview(sourceName, bytes, validation.Package, _manager.BuildManifestDrift(validation.Package)));
    }

    public PluginExtensionCenterInstallResult InstallPackage(string packagePath)
    {
        var review = ReviewPackage(packagePath);
        return review.Ok && review.Review is not null
            ? InstallReviewedPackage(review.Review)
            : new PluginExtensionCenterInstallResult(false, null, review.Diagnostics, review.Code, review.Message);
    }

    public PluginExtensionCenterInstallResult InstallReviewedPackage(PluginExtensionCenterPackageReview review)
    {
        ArgumentNullException.ThrowIfNull(review);
        var installed = _manager.InstallPersisted(review.Package, review.Bytes);
        return installed.Ok && installed.Entry is not null
            ? PluginExtensionCenterInstallResult.Success(installed.Entry)
            : PluginExtensionCenterInstallResult.Failure(installed.Code ?? "install_failed", installed.Message ?? "Extension package could not be installed.");
    }

    public bool Uninstall(string pluginId) => _manager.UninstallPersisted(pluginId);

    public static PluginExtensionCenterEntrySummary Summarize(PluginClientExtensionCacheEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var client = entry.DeploymentPlan.Client;
        var resources = new List<string>();
        if (client?.Ui is not null)
            resources.Add(client.Ui.Path);
        if (client?.Theme is not null)
            resources.Add(client.Theme.Path);
        if (client?.Script is not null)
            resources.Add(client.Script.Path);
        if (client is not null)
            resources.AddRange(client.Resources.Select(resource => resource.Path));

        return new PluginExtensionCenterEntrySummary(
            entry.Id,
            entry.Version,
            entry.UiSchema is not null,
            new ReadOnlyCollection<string>(entry.Commands.Select(command => command.Id).ToArray()),
            new ReadOnlyCollection<string>(entry.HostCapabilities.ToArray()),
            new ReadOnlyCollection<string>(entry.EventPermissions.ToArray()),
            new ReadOnlyCollection<string>(resources.Order(StringComparer.Ordinal).ToArray()),
            entry.DeploymentPlan.Daemon?.Plugin?.Path);
    }
}

public sealed record PluginExtensionCenterEntrySummary(
    string Id,
    string Version,
    bool HasUi,
    IReadOnlyList<string> Commands,
    IReadOnlyList<string> HostCapabilities,
    IReadOnlyList<string> EventPermissions,
    IReadOnlyList<string> Resources,
    string? DaemonPluginPath);
