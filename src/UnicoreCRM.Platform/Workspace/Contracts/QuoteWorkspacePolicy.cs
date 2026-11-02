namespace UnicoreCRM.Platform.Workspace.Contracts;

public sealed record QuoteWorkspacePolicy(bool Enabled, IReadOnlyList<string> EnabledCurrencies, long Version);
public interface IQuoteWorkspacePolicyReader
{
    Task<QuoteWorkspacePolicy?> ReadAsync(string workspaceId, CancellationToken cancellationToken);
}
