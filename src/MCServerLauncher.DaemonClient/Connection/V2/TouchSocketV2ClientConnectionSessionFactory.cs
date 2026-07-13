using System;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.DaemonClient.State;

namespace MCServerLauncher.DaemonClient.Connection.V2;

internal sealed class TouchSocketV2ClientConnectionSessionFactory : IV2ClientConnectionSessionFactory
{
    private readonly Uri _endpoint;
    private readonly string _token;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _requestTimeout;

    internal TouchSocketV2ClientConnectionSessionFactory(
        Uri endpoint,
        string token,
        TimeProvider? timeProvider = null,
        TimeSpan? requestTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        if (!endpoint.IsAbsoluteUri ||
            (endpoint.Scheme != Uri.UriSchemeWs && endpoint.Scheme != Uri.UriSchemeWss))
        {
            throw new ArgumentException("The V2 endpoint must be an absolute ws or wss URI.", nameof(endpoint));
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

        var timeout = requestTimeout ?? TimeSpan.FromSeconds(30);
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));

        _endpoint = endpoint;
        _token = token;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _requestTimeout = timeout;
    }

    public IV2ClientConnectionSession Create(
        RemoteInstanceCatalogMirror mirror,
        Action<V2ClientConnectionCoordinator, JsonRpcRemoteEventNotification> routeEvent,
        Action<V2ClientDiagnostic>? diagnostic = null) =>
        new TouchSocketV2ClientConnectionSession(
            _endpoint,
            _token,
            mirror,
            routeEvent,
            _timeProvider,
            _requestTimeout,
            diagnostic);
}
