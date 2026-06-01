using MCServerLauncher.Common.ProtoType.Instance;

namespace MCServerLauncher.Common.Extensibility;

/// <summary>
/// Extensible interface for instance creation providers.
/// Implementations provide InstanceType and TargetType metadata.
/// The WPF-local ICreateInstanceStep interface handles step orchestration.
/// </summary>
public interface ICreateInstanceProvider
{
    string Id => GetType().Name;
    string DisplayName => Id;
    InstanceType InstanceType { get; }
    TargetType TargetType { get; }
}
