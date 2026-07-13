using System;
using System.Threading;
using System.Threading.Tasks;
using MCServerLauncher.Daemon.API.Errors;
using RustyOptions;

namespace MCServerLauncher.DaemonClient.Connection.V2;

internal enum V2ClientInvocationDisposition
{
    NotAdmitted,
    ResponseReceived,
    AdmittedWithoutAuthoritativeResponse
}

/// <summary>
/// Publishes whether a completed invocation crossed the external send boundary.
/// </summary>
internal sealed class V2ClientInvocationOutcome
{
    private const int PendingNotAdmitted = 0;
    private const int PendingAdmitted = 1;
    private const int CompletedNotAdmitted = 2;
    private const int CompletedResponseReceived = 3;
    private const int CompletedAdmittedWithoutResponse = 4;

    private int _state;

    internal bool IsCompleted => Volatile.Read(ref _state) >= CompletedNotAdmitted;

    internal V2ClientInvocationDisposition Disposition => Volatile.Read(ref _state) switch
    {
        CompletedNotAdmitted => V2ClientInvocationDisposition.NotAdmitted,
        CompletedResponseReceived => V2ClientInvocationDisposition.ResponseReceived,
        CompletedAdmittedWithoutResponse => V2ClientInvocationDisposition.AdmittedWithoutAuthoritativeResponse,
        _ => throw new InvalidOperationException("The invocation outcome is not terminal.")
    };

    internal bool TryMarkAdmitted()
    {
        while (true)
        {
            var state = Volatile.Read(ref _state);
            if (state == PendingAdmitted)
                return true;
            if (state >= CompletedNotAdmitted)
                return false;
            if (Interlocked.CompareExchange(ref _state, PendingAdmitted, PendingNotAdmitted) == PendingNotAdmitted)
                return true;
        }
    }

    internal bool TryComplete(bool authoritativeResponse)
    {
        while (true)
        {
            var state = Volatile.Read(ref _state);
            if (state >= CompletedNotAdmitted)
                return false;

            var completedState = authoritativeResponse
                ? CompletedResponseReceived
                : state == PendingAdmitted
                    ? CompletedAdmittedWithoutResponse
                    : CompletedNotAdmitted;
            if (Interlocked.CompareExchange(ref _state, completedState, state) == state)
                return true;
        }
    }
}

internal sealed class V2ClientInvocationOperation<TResult>
    where TResult : notnull
{
    internal V2ClientInvocationOperation(
        Task<Result<TResult, DaemonError>> completion,
        V2ClientInvocationOutcome outcome)
    {
        Completion = completion ?? throw new ArgumentNullException(nameof(completion));
        Outcome = outcome ?? throw new ArgumentNullException(nameof(outcome));
    }

    internal Task<Result<TResult, DaemonError>> Completion { get; }

    internal V2ClientInvocationOutcome Outcome { get; }
}
