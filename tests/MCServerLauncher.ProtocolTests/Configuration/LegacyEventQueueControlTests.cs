using MCServerLauncher.Daemon.Bootstrap;

namespace MCServerLauncher.ProtocolTests;

public sealed class LegacyEventQueueControlTests
{
    [Fact]
    public async Task Control_ForwardsStopAndDrainWithoutOwningParticipantDisposal()
    {
        var control = new LegacyEventQueueControl();
        var participant = new RecordingParticipant();
        control.Attach(participant);

        control.StopAccepting();
        control.StopAccepting();
        using var cancellation = new CancellationTokenSource();
        await control.DrainAsync(cancellation.Token);

        Assert.Equal(1, participant.StopCalls);
        Assert.Equal(cancellation.Token, participant.DrainToken);
    }

    [Fact]
    public async Task Control_RequiresExactlyOneParticipant()
    {
        var control = new LegacyEventQueueControl();
        Assert.Throws<InvalidOperationException>(control.StopAccepting);
        await Assert.ThrowsAsync<InvalidOperationException>(() => control.DrainAsync(CancellationToken.None));

        control.Attach(new RecordingParticipant());
        Assert.Throws<InvalidOperationException>(() => control.Attach(new RecordingParticipant()));
    }

    private sealed class RecordingParticipant : ILegacyEventQueueParticipant
    {
        public int StopCalls { get; private set; }
        public CancellationToken DrainToken { get; private set; }

        public void StopAccepting()
        {
            StopCalls++;
        }

        public Task DrainAsync(CancellationToken cancellationToken)
        {
            DrainToken = cancellationToken;
            return Task.CompletedTask;
        }
    }
}
