using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Operations.Tasks.Contracts;

/// <summary>Module-owned facts only. AccessControl remains the sole scope/capability authority.</summary>
public interface IActivityReferenceAuthority : IRecordAccessFactProvider
{
    // Trusted, static SQL owned by the business persistence implementation. It projects only
    // WorkspaceId, RecordId, OwnerId, RelationshipType, RelationshipId and CanLog;
    // CanLog includes owner lifecycle and current relationship validity. Never client SQL.
    string ReadFactsSql { get; }
    Task<bool> IsActivityReferenceValidAsync(TrustedWorkspaceContext workspace, string recordId,
        BuyerReference? relationship, bool forWrite, CancellationToken cancellationToken);
}

public sealed record ActivityReadScope(string ModuleKey, string FactsSql, RecordAccessAuthorization Authorization);
