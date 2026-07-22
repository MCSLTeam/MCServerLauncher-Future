using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;

namespace MCServerLauncher.Daemon.Remote.Rpc.Catalog;

internal static class RpcCallerSubjectResolver
{
    internal static bool TryResolve(
        ProtocolInvocationContext context,
        bool useGlobalOwnerForMainToken,
        out string subject,
        out DaemonError? error)
    {
        ArgumentNullException.ThrowIfNull(context);
        subject = string.Empty;
        error = null;
        var view = context.PermissionView;
        if (view is null || string.IsNullOrWhiteSpace(view.Subject))
        {
            error = new PermissionDaemonError(
                "auth.subject_required",
                "Connection subject is required for ownership binding.");
            return false;
        }

        if (view.IsMainToken)
        {
            if (!PrincipalIdentityPolicy.IsMainTokenSubject(view.Subject))
            {
                error = new PermissionDaemonError(
                    "auth.subject_invalid",
                    "The main-token connection subject is invalid.");
                return false;
            }

            subject = useGlobalOwnerForMainToken
                ? PrincipalIdentityPolicy.GlobalOwnerPrincipal
                : PrincipalIdentityPolicy.MainTokenSubject;
            return true;
        }

        if (!PrincipalIdentityPolicy.IsValidExternalSubject(view.Subject))
        {
            error = new PermissionDaemonError(
                "auth.subject_reserved",
                "The connection subject is reserved for a daemon-owned identity.");
            return false;
        }

        subject = view.Subject;
        return true;
    }
}
