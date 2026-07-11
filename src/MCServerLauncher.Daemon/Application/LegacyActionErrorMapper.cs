using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.Remote.Action;

namespace MCServerLauncher.Daemon.ApplicationCore;

internal static class LegacyActionErrorMapper
{
    internal static ActionError ToActionError(DaemonError error, ActionRetcode fallback)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(fallback);

        var retcode = error.Code switch
        {
            "instance.not_found" => ActionRetcode.InstanceNotFound,
            "instance.running" or "instance.not_running" => ActionRetcode.BadInstanceState,
            "instance.start_failed" => ActionRetcode.ProcessError,
            "instance.invalid" or "instance.command_empty" or "instance.settings_invalid" => ActionRetcode.BadRequest,
            _ => fallback
        };

        return new ActionError(retcode.WithMessage(error.Message));
    }
}
