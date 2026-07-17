using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace MCServerLauncher.Daemon.API.State;

/// <summary>
/// Immutable catalog of instance snapshots.
/// </summary>
public sealed class InstanceCatalogSnapshot
{
    public static InstanceCatalogSnapshot Empty { get; } = new(ImmutableDictionary<Guid, InstanceSnapshot>.Empty);
    private readonly ImmutableDictionary<Guid, InstanceSnapshot> _instances;

    public InstanceCatalogSnapshot(IEnumerable<KeyValuePair<Guid, InstanceSnapshot>> instances)
    {
        ArgumentNullException.ThrowIfNull(instances);

        var builder = ImmutableDictionary.CreateBuilder<Guid, InstanceSnapshot>();
        foreach (var pair in instances)
        {
            if (pair.Key == Guid.Empty)
            {
                throw new ArgumentException("An instance catalog key cannot be empty.", nameof(instances));
            }

            if (pair.Value is null)
            {
                throw new ArgumentException("An instance catalog cannot contain a null snapshot.", nameof(instances));
            }

            if (pair.Value.Id == Guid.Empty)
            {
                throw new ArgumentException("An instance snapshot identifier cannot be empty.", nameof(instances));
            }

            if (pair.Key != pair.Value.Id)
            {
                throw new ArgumentException("An instance catalog key must match its snapshot identifier.", nameof(instances));
            }

            if (builder.ContainsKey(pair.Key))
            {
                throw new ArgumentException("An instance catalog cannot contain duplicate identifiers.", nameof(instances));
            }

            builder.Add(pair.Key, pair.Value);
        }

        _instances = builder.ToImmutable();
    }

    public ImmutableDictionary<Guid, InstanceSnapshot> Instances => _instances;

    public bool TryGet(Guid instanceId, [NotNullWhen(true)] out InstanceSnapshot? snapshot) =>
        _instances.TryGetValue(instanceId, out snapshot);
}
