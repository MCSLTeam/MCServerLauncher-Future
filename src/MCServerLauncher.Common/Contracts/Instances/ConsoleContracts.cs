using System.Collections.Immutable;

namespace MCServerLauncher.Common.Contracts.Instances;

public sealed record ConsoleOpenRequest(Guid InstanceId, ushort Columns = 120, ushort Rows = 40);

public sealed record ConsoleSession(
    Guid SessionId,
    Guid InstanceId,
    DateTimeOffset ExpiresAt,
    int MaxChunkSize,
    ushort Columns,
    ushort Rows);

public sealed record ConsoleSessionReference(Guid SessionId);

public sealed record ConsoleResizeRequest(Guid SessionId, ushort Columns, ushort Rows);
