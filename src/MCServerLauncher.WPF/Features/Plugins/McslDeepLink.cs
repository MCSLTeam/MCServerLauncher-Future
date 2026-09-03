using Microsoft.Win32;
using System;
using System.Runtime.InteropServices;

namespace MCServerLauncher.WPF.Features.Plugins;

/// <summary>Result of parsing a `mcsl://` deep link.</summary>
public sealed record McslDeepLinkPayload(string Kind, string PackageId, string? Version)
{
    public static McslDeepLinkPayload? None => null;
}

/// <summary>
/// Registers and parses the `mcsl://` URI scheme used by the plugin market
/// to hand install requests to the desktop client, e.g.
/// `mcsl://install/community.example.status-panel/1.2.0`.
/// </summary>
public static class McslDeepLink
{
    public static readonly string Scheme = "mcsl";

    /// <summary>Set by <see cref="App.OnStartup"/> from the launch arguments.</summary>
    public static McslDeepLinkPayload? Pending { get; set; }

    /// <summary>
    /// Register the URI scheme under HKEY_CURRENT_USER so `mcsl://…` links
    /// launch this executable. Best-effort; failures are non-fatal.
    /// </summary>
    public static void RegisterProtocol()
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath))
                return;
            using var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes\mcsl");
            classes.SetValue("", "URL:MCSL Plugin Install Protocol");
            classes.SetValue("URL Protocol", "");
            using var command = classes.CreateSubKey(@"shell\open\command");
            command.SetValue("", $"\"{exePath}\" \"%1\"");
        }
        catch (Exception exception)
        {
            // The launcher must still start even if registration fails.
            Serilog.Log.Warning("Failed to register mcsl:// protocol: {Error}", exception.Message);
        }
    }

    /// <summary>Parse `mcsl://install/&lt;packageId&gt;[/&lt;version&gt;]`.</summary>
    public static McslDeepLinkPayload? TryParse(string? argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
            return null;
        if (!Uri.TryCreate(argument, UriKind.Absolute, out var uri))
            return null;
        if (!string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase))
            return null;

        var path = uri.AbsolutePath.Trim('/');
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length is < 2 or > 3)
            return null;
        if (!string.Equals(segments[0], "install", StringComparison.OrdinalIgnoreCase))
            return null;

        return new McslDeepLinkPayload("install", Uri.UnescapeDataString(segments[1]), segments.Length == 3 ? Uri.UnescapeDataString(segments[2]) : null);
    }

    public static string BuildInstallUri(string packageId, string? version) =>
        version is null
            ? $"mcsl://install/{Uri.EscapeDataString(packageId)}"
            : $"mcsl://install/{Uri.EscapeDataString(packageId)}/{Uri.EscapeDataString(version)}";
}
