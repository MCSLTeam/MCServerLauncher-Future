using System.Collections.Immutable;

namespace MCServerLauncher.Common.Contracts.Auth;

public sealed record TokenIssueRequest(
    string Subject,
    string Audience,
    ImmutableArray<string> Permissions,
    int TtlSeconds);

public sealed record TokenIssueResult(
    string Token,
    string Subject,
    string Audience,
    ImmutableArray<string> Permissions,
    DateTimeOffset ExpiresAt,
    string TokenId);
