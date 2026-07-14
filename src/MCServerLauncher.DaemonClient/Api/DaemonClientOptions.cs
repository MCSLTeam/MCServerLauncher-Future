using System;

namespace MCServerLauncher.DaemonClient;

public sealed class DaemonClientOptions
{
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultReconnectDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan MaximumReconnectDelay =
        TimeSpan.FromMilliseconds(uint.MaxValue - 1d);

    public DaemonClientOptions(
        Uri endpoint,
        string token,
        TimeSpan? requestTimeout = null,
        TimeSpan? reconnectDelay = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        if (!endpoint.IsAbsoluteUri ||
            (endpoint.Scheme != Uri.UriSchemeWs && endpoint.Scheme != Uri.UriSchemeWss) ||
            string.IsNullOrEmpty(endpoint.Host))
        {
            throw new ArgumentException(
                "The V2 endpoint must be an absolute ws or wss URI with a host.",
                nameof(endpoint));
        }

        if (!StringComparer.Ordinal.Equals(endpoint.AbsolutePath, "/api/v2") ||
            !string.IsNullOrEmpty(endpoint.Query) ||
            !string.IsNullOrEmpty(endpoint.Fragment) ||
            !string.IsNullOrEmpty(endpoint.UserInfo))
        {
            throw new ArgumentException(
                "The V2 endpoint must identify the exact /api/v2 path without credentials, query, or fragment.",
                nameof(endpoint));
        }

        var effectiveRequestTimeout = requestTimeout ?? DefaultRequestTimeout;
        if (effectiveRequestTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));

        var effectiveReconnectDelay = reconnectDelay ?? DefaultReconnectDelay;
        if (effectiveReconnectDelay < TimeSpan.Zero || effectiveReconnectDelay > MaximumReconnectDelay)
            throw new ArgumentOutOfRangeException(nameof(reconnectDelay));

        Endpoint = endpoint;
        Token = token;
        RequestTimeout = effectiveRequestTimeout;
        ReconnectDelay = effectiveReconnectDelay;
    }

    public Uri Endpoint { get; }

    public string Token { get; }

    public TimeSpan RequestTimeout { get; }

    public TimeSpan ReconnectDelay { get; }
}
