namespace UnicoreCRM.Sales.Quotes.Application.Common;

/// <summary>Quotes-only clock. A fixed clock is confined to one explicitly configured Development test Workspace.</summary>
internal sealed class QuoteLifecycleClock(TimeProvider system, string? verificationWorkspace = null, DateTimeOffset? verificationTime = null)
{
    internal DateTimeOffset GetUtcNow(string workspaceId) => IsVerificationFor(workspaceId) ? verificationTime!.Value : system.GetUtcNow();
    internal bool IsVerificationFor(string workspaceId) => verificationTime.HasValue && workspaceId == verificationWorkspace;
}
