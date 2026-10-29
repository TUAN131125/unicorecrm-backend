using UnicoreCRM.Operations.Tasks.Contracts;
using UnicoreCRM.Operations.Tasks.Domain;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Operations.Tasks.Application.Common;

internal sealed class ActivityRecordAccess(
    IRecordAccessEvaluator evaluator,
    IEnumerable<IActivityReferenceAuthority> authorities)
{
    private readonly IReadOnlyDictionary<string, IActivityReferenceAuthority> owners =
        authorities.ToDictionary(owner => owner.Descriptor.ResourceKey, StringComparer.Ordinal);

    internal async Task<IReadOnlyList<ActivityReadScope>> ReadScopesAsync(TaskAccess access,
        TaskRequestMetadata metadata, CancellationToken cancellationToken)
    {
        var scopes = new List<ActivityReadScope>();
        foreach (var owner in owners.Values)
        {
            var descriptor = owner.Descriptor;
            var decision = await evaluator.AuthorizeResourceAsync(descriptor.ResourceKey, descriptor.ReadCapability,
                descriptor.EnforceableFields.Keys.ToArray(), RecordAccessRepresentation.Full,
                new RecordAccessRequestContext(metadata.RequestId, metadata.CorrelationId), cancellationToken);
            if (decision.IsAllowed && decision.HoldsResourceRead
                && decision.TrustedWorkspace?.WorkspaceId == access.Trusted.WorkspaceId
                && decision.ScopeFilter is RecordAccessScopeFilter.Workspace or RecordAccessScopeFilter.OwnedByMember
                && decision.UnenforceableFieldKeys.Count == 0
                && descriptor.EnforceableFields.Keys.All(decision.CanRead))
                scopes.Add(new(descriptor.ResourceKey, owner.ReadFactsSql, decision));
        }
        return scopes;
    }

    // A relationship-only Contact/Organization activity is supported by resolving that exact
    // target. Multiple references must be a relationship proved by the record's owner AND every
    // target must independently pass current read, scope and field security.
    internal async Task<bool> CanAccessAsync(TaskAccess access, TaskReferenceData references,
        TaskRequestMetadata metadata, bool forWrite, CancellationToken cancellationToken)
    {
        var module = references.RecordModuleKey ?? RelationshipModule(references.RelationshipType);
        var id = references.RecordId ?? references.RelationshipId;
        if (module is null || id is null || !owners.TryGetValue(module, out var owner)
            || !ValidSource(references, module, id)) return false;
        var relationship = references.RelationshipType is null ? null
            : new BuyerReference(references.RelationshipType, references.RelationshipId!);
        if (!await AuthorizeAsync(owner, id, access, metadata, cancellationToken)
            || !await owner.IsActivityReferenceValidAsync(access.Trusted, id, relationship, forWrite, cancellationToken))
            return false;
        if (relationship is not null)
        {
            var relatedModule = RelationshipModule(relationship.Type);
            if (relatedModule is null || relationship.Id is null || !owners.TryGetValue(relatedModule, out var relatedOwner)
                || !await AuthorizeAsync(relatedOwner, relationship.Id, access, metadata, cancellationToken)
                || !await relatedOwner.IsActivityReferenceValidAsync(access.Trusted, relationship.Id, null, forWrite, cancellationToken))
                return false;
        }
        return true;
    }

    private async Task<bool> AuthorizeAsync(IActivityReferenceAuthority owner, string id, TaskAccess access,
        TaskRequestMetadata metadata, CancellationToken cancellationToken)
    {
        var descriptor = owner.Descriptor;
        var context = new RecordAccessRequestContext(metadata.RequestId, metadata.CorrelationId);
        var decision = await evaluator.AuthorizeResourceAsync(descriptor.ResourceKey, descriptor.ReadCapability,
            descriptor.EnforceableFields.Keys.ToArray(), RecordAccessRepresentation.Full, context, cancellationToken);
        if (!decision.IsAllowed || decision.TrustedWorkspace?.WorkspaceId != access.Trusted.WorkspaceId
            || decision.UnenforceableFieldKeys.Count != 0
            // Free text can quote any protected owner field. Withhold the entire activity when
            // any owner field cannot be read, including count/search, rather than guess redactions.
            || descriptor.EnforceableFields.Keys.Any(field => !decision.CanRead(field))) return false;
        var facts = await owner.ReadFactsAsync(access.Trusted, id, context, cancellationToken);
        return (await evaluator.AuthorizeRecordAsync(decision, id, facts, "activity.reference", context, cancellationToken)).IsAllowed;
    }

    internal static string? RelationshipModule(string? type) => type switch
    {
        "CONTACT" => "contacts", "ORGANIZATION_ACCOUNT" => "organizations", _ => null
    };

    internal static bool ValidSource(TaskReferenceData references, string? module, string? id) =>
        references.SourceType is null
            ? references.SourceId is null && references.SourceEvidence is null
            : references.SourceId == id && references.SourceEvidence is null && (module, references.SourceType) is
                ("leads", "LEAD_ACTIVITY" or "LEAD_DETAIL") or
                ("contacts", "CONTACT_ACTIVITY" or "CONTACT_DETAIL") or
                ("organizations", "ORGANIZATION_ACTIVITY" or "ORGANIZATION_DETAIL") or
                ("customers", "CUSTOMER_ACTIVITY" or "CUSTOMER_DETAIL" or "CUSTOMER_360");
}
