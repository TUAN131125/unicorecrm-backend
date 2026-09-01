namespace UnicoreCRM.Platform.IdentityAuth.Contracts;

public interface IIdentityAccessDirectoryProfileSource
{
    Task<IReadOnlyList<IdentityAccessDirectoryProfile>> ReadAsync(
        IReadOnlyList<string> accountIds,
        CancellationToken cancellationToken);
}

public sealed record IdentityAccessDirectoryProfile(
    string AccountId,
    string DisplayName,
    string? Email,
    string Status,
    DateTimeOffset? ProvisionedAt);
