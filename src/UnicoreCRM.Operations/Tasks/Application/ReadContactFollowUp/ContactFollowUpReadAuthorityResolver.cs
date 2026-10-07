using UnicoreCRM.Operations.Tasks.Application.Common;
using UnicoreCRM.Operations.Tasks.Contracts;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Operations.Tasks.Application.ReadContactFollowUp;

internal sealed class ContactFollowUpReadAuthorityResolver(TaskAuthorization authorization)
    : IContactFollowUpReadAuthorityResolver
{
    public async Task<ContactFollowUpAuthorityResult> ResolveAsync(
        string requestId, string correlationId, CancellationToken cancellationToken)
    {
        var result = await authorization.AuthorizeAsync(TaskCapabilities.Read,
            new TaskRequestMetadata(requestId, correlationId), cancellationToken);
        if (!result.IsSuccess)
            return new(null, result.Error!.Code);

        var access = result.Value!;
        // Eligibility and association themselves disclose these values, even when the Task row
        // is not returned. A withheld/masked field cannot participate in this derived result.
        if (new[] { "recordRef", "dueAt", "assigneeId", "status", "archivedAt" }
            .Any(field => !access.Authorization.CanRead(field)))
            return new(null, "ACCESS_DENIED");

        switch (access.Authorization.ScopeFilter)
        {
            case RecordAccessScopeFilter.Workspace:
                return new(new(access.Trusted.WorkspaceId, null), null);
            case RecordAccessScopeFilter.OwnedByMember
                when !string.IsNullOrWhiteSpace(access.Authorization.ScopeOwnerMemberId):
                return new(new(access.Trusted.WorkspaceId, access.Authorization.ScopeOwnerMemberId), null);
            default:
                return new(null, "ACCESS_DENIED");
        }
    }
}
