using MCServerLauncher.Common.Contracts.Operations;

namespace MCServerLauncher.Daemon.API.Application;

/// <summary>
/// Progress-only operation context. Terminal status is owned by the coordinator.
/// </summary>
public interface IOperationContext
{
    Guid OperationId { get; }

    void SetStage(OperationStage stage);

    void ReportProgress(OperationProgress progress);

    IOperationContext CreateChild(string name, double weight);
}

/// <summary>
/// Shared no-op context for intentional non-operation internal paths.
/// </summary>
public sealed class NoOpOperationContext : IOperationContext
{
    public static NoOpOperationContext Instance { get; } = new();

    private NoOpOperationContext()
    {
    }

    public Guid OperationId { get; } = Guid.Empty;

    public void SetStage(OperationStage stage)
    {
        _ = stage;
    }

    public void ReportProgress(OperationProgress progress)
    {
        _ = progress;
    }

    public IOperationContext CreateChild(string name, double weight)
    {
        _ = name;
        _ = weight;
        return this;
    }
}
