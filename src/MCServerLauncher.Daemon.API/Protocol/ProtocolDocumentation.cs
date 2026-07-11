namespace MCServerLauncher.Daemon.API.Protocol;

/// <summary>
/// Stable documentation metadata for an RPC definition.
/// </summary>
public sealed class RpcDocumentation
{
    public RpcDocumentation(
        string category,
        string summary,
        string description,
        string requestSchemaId,
        string resultSchemaId)
    {
        Category = Require(category, nameof(category));
        Summary = Require(summary, nameof(summary));
        Description = Require(description, nameof(description));
        RequestSchemaId = Require(requestSchemaId, nameof(requestSchemaId));
        ResultSchemaId = Require(resultSchemaId, nameof(resultSchemaId));
    }

    public string Category { get; }

    public string Summary { get; }

    public string Description { get; }

    public string RequestSchemaId { get; }

    public string ResultSchemaId { get; }

    private static string Require(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }
}

/// <summary>
/// Stable documentation metadata for a server event definition.
/// </summary>
public sealed class EventDocumentation
{
    public EventDocumentation(
        string category,
        string summary,
        string description,
        string dataSchemaId,
        string? metaSchemaId)
    {
        Category = Require(category, nameof(category));
        Summary = Require(summary, nameof(summary));
        Description = Require(description, nameof(description));
        DataSchemaId = Require(dataSchemaId, nameof(dataSchemaId));
        MetaSchemaId = metaSchemaId is null ? null : Require(metaSchemaId, nameof(metaSchemaId));
    }

    public string Category { get; }

    public string Summary { get; }

    public string Description { get; }

    public string DataSchemaId { get; }

    public string? MetaSchemaId { get; }

    private static string Require(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }
}
