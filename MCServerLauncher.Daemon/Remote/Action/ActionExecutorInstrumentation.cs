namespace MCServerLauncher.Daemon.Remote.Action;

internal interface IActionExecutorInstrumentation
{
    void OnQueueSubmitted();
    void OnQueueRejected();
    void OnQueueWaitObserved(TimeSpan wait);
    void OnHandlerCompleted(TimeSpan duration, bool success, bool canceled);
    void OnSendCompleted(TimeSpan duration, bool success, bool canceled);
}

internal sealed class NoopActionExecutorInstrumentation : IActionExecutorInstrumentation
{
    public static readonly NoopActionExecutorInstrumentation Instance = new();

    public void OnQueueSubmitted()
    {
    }

    public void OnQueueRejected()
    {
    }

    public void OnQueueWaitObserved(TimeSpan wait)
    {
    }

    public void OnHandlerCompleted(TimeSpan duration, bool success, bool canceled)
    {
    }

    public void OnSendCompleted(TimeSpan duration, bool success, bool canceled)
    {
    }
}

internal readonly record struct ActionExecutorInstrumentationSnapshot(
    long QueueSubmittedCount,
    long QueueRejectedCount,
    long QueueWaitSampleCount,
    long QueueWaitTotalTicks,
    long HandlerDurationSampleCount,
    long HandlerDurationTotalTicks,
    long HandlerSuccessCount,
    long HandlerFailureCount,
    long HandlerCancellationCount,
    long SendDurationSampleCount,
    long SendDurationTotalTicks,
    long SendSuccessCount,
    long SendFailureCount,
    long SendCancellationCount);

internal sealed class ActionExecutorInstrumentationCollector : IActionExecutorInstrumentation
{
    private long _queueSubmittedCount;
    private long _queueRejectedCount;
    private long _queueWaitSampleCount;
    private long _queueWaitTotalTicks;

    private long _handlerDurationSampleCount;
    private long _handlerDurationTotalTicks;
    private long _handlerSuccessCount;
    private long _handlerFailureCount;
    private long _handlerCancellationCount;

    private long _sendDurationSampleCount;
    private long _sendDurationTotalTicks;
    private long _sendSuccessCount;
    private long _sendFailureCount;
    private long _sendCancellationCount;

    public void OnQueueSubmitted()
    {
        Interlocked.Increment(ref _queueSubmittedCount);
    }

    public void OnQueueRejected()
    {
        Interlocked.Increment(ref _queueRejectedCount);
    }

    public void OnQueueWaitObserved(TimeSpan wait)
    {
        Interlocked.Increment(ref _queueWaitSampleCount);
        Interlocked.Add(ref _queueWaitTotalTicks, wait.Ticks);
    }

    public void OnHandlerCompleted(TimeSpan duration, bool success, bool canceled)
    {
        Interlocked.Increment(ref _handlerDurationSampleCount);
        Interlocked.Add(ref _handlerDurationTotalTicks, duration.Ticks);

        if (success)
        {
            Interlocked.Increment(ref _handlerSuccessCount);
            return;
        }

        if (canceled)
        {
            Interlocked.Increment(ref _handlerCancellationCount);
            return;
        }

        Interlocked.Increment(ref _handlerFailureCount);
    }

    public void OnSendCompleted(TimeSpan duration, bool success, bool canceled)
    {
        Interlocked.Increment(ref _sendDurationSampleCount);
        Interlocked.Add(ref _sendDurationTotalTicks, duration.Ticks);

        if (success)
        {
            Interlocked.Increment(ref _sendSuccessCount);
            return;
        }

        if (canceled)
        {
            Interlocked.Increment(ref _sendCancellationCount);
            return;
        }

        Interlocked.Increment(ref _sendFailureCount);
    }

    public ActionExecutorInstrumentationSnapshot Snapshot()
    {
        return new ActionExecutorInstrumentationSnapshot(
            Interlocked.Read(ref _queueSubmittedCount),
            Interlocked.Read(ref _queueRejectedCount),
            Interlocked.Read(ref _queueWaitSampleCount),
            Interlocked.Read(ref _queueWaitTotalTicks),
            Interlocked.Read(ref _handlerDurationSampleCount),
            Interlocked.Read(ref _handlerDurationTotalTicks),
            Interlocked.Read(ref _handlerSuccessCount),
            Interlocked.Read(ref _handlerFailureCount),
            Interlocked.Read(ref _handlerCancellationCount),
            Interlocked.Read(ref _sendDurationSampleCount),
            Interlocked.Read(ref _sendDurationTotalTicks),
            Interlocked.Read(ref _sendSuccessCount),
            Interlocked.Read(ref _sendFailureCount),
            Interlocked.Read(ref _sendCancellationCount));
    }
}
