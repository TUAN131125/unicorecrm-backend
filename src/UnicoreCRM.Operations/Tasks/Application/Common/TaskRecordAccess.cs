using UnicoreCRM.Operations.Tasks.Contracts;
using UnicoreCRM.Operations.Tasks.Domain;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Operations.Tasks.Application.Common;

/// <summary>
/// The authorization result every Tasks use case works from: the trusted Workspace plus the
/// AccessControl decision that governs this resource for this caller.
/// </summary>
internal sealed record TaskAccess(TrustedWorkspaceContext Trusted, RecordAccessAuthorization Authorization);

/// <summary>
/// Tasks-side enforcement of the AccessControl field-security decision. Tasks decides nothing here:
/// AccessControl has already reduced the policy to a per-field <see cref="RecordFieldEnforcement"/>,
/// and this type only applies it to the Tasks wire vocabulary, which is the one thing AccessControl
/// cannot know. The representation rules are the ones frozen for Support.
/// </summary>
internal static class TaskFieldSecurity
{
    /// <summary>
    /// The field keys Tasks can enforce a policy on, mapped to whether the <c>TaskReadModel</c> wire
    /// contract makes the field required.
    ///
    /// <para>These are the <c>TaskReadModel</c> property names, not the frontend form names. The
    /// frontend requests <c>recordRef</c> and <c>assigneeId</c>, which do exist here; the mapping
    /// between the frontend form vocabulary and these names is an <c>AUTHORITY_GAP</c>, so a key the
    /// frontend asks for that Tasks does not project fails closed rather than resolving.</para>
    ///
    /// <para>Two rules, frozen and distinct. A policy naming a key <b>outside</b> this vocabulary is
    /// not readable and not writable - the key fails closed and the public evaluation reports it
    /// HIDDEN - and does not by itself refuse the operation, because this owner never projects it.
    /// A policy naming a key <b>inside</b> this vocabulary that the representation being returned
    /// makes required cannot be honoured at all, and refuses the operation rather than returning a
    /// value the policy forbids.</para>
    /// </summary>
    internal static IReadOnlyDictionary<string, bool> EnforceableFields { get; } =
        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = true,
            ["title"] = true,
            ["status"] = true,
            ["priority"] = true,
            ["assigneeId"] = true,
            ["dueAt"] = true,
            ["createdAt"] = true,
            ["updatedAt"] = true,
            ["resourceVersion"] = true,
            ["description"] = false,
            ["completedAt"] = false,
            ["cancelledAt"] = false,
            ["cancellationReason"] = false,
            ["outcome"] = false,
            ["relationshipRef"] = false,
            ["recordRef"] = false,
            ["sourceRef"] = false,
            ["archivedAt"] = false,
            ["archiveReason"] = false
        };

    internal static IReadOnlyList<string> FieldKeys { get; } = EnforceableFields.Keys.Order(StringComparer.Ordinal).ToArray();

    internal static TaskReadModel Project(TaskReadModel model, RecordAccessAuthorization access) =>
        model with
        {
            Description = access.CanRead("description") ? model.Description : null,
            CompletedAt = access.CanRead("completedAt") ? model.CompletedAt : null,
            CancelledAt = access.CanRead("cancelledAt") ? model.CancelledAt : null,
            CancellationReason = access.CanRead("cancellationReason") ? model.CancellationReason : null,
            Outcome = access.CanRead("outcome") ? model.Outcome : null,
            RelationshipRef = access.CanRead("relationshipRef") ? model.RelationshipRef : null,
            RecordRef = access.CanRead("recordRef") ? model.RecordRef : null,
            SourceRef = access.CanRead("sourceRef") ? model.SourceRef : null,
            ArchivedAt = access.CanRead("archivedAt") ? model.ArchivedAt : null,
            ArchiveReason = access.CanRead("archiveReason") ? model.ArchiveReason : null
        };

    /// <summary>
    /// The refusal a caller receives when a restrictive policy names a field the representation being
    /// returned cannot omit. AccessControl decides that, against the representation the operation
    /// declared; this owner only applies the answer.
    /// </summary>
    internal static TaskOperationError? UnenforceablePolicy(RecordAccessAuthorization access) =>
        access.UnenforceableFieldKeys.Count == 0
            ? null
            : new TaskOperationError(
                "ACCESS_DENIED",
                403,
                "Access denied",
                "A field-security policy applies to a field this resource cannot withhold, so the request is refused rather than returning a value the policy forbids.");

    /// <summary>
    /// Refuses a creation that populates a field the caller may not write. Creation has no prior
    /// record and therefore no record scope, but field-write policy still governs what may be
    /// written: a HIDDEN, MASKED or READ_ONLY field supplied on the way in is refused rather than
    /// silently dropped, because silently dropping it would return a record that does not match the
    /// request the caller believes it made. There is no stored value to compare against, so every
    /// field the request actually sets counts as a write.
    /// </summary>
    internal static TaskOperationError? GuardCreateWrite(
        RecordAccessAuthorization access,
        string? description,
        TaskReferenceData references)
    {
        // title, priority, assigneeId and dueAt are required by the create contract and are always
        // written. A non-writable required create field therefore fails the creation closed: there
        // is no admitted representation of a create that omits them.
        var written = new List<string> { "title", "priority", "assigneeId", "dueAt" };
        if (description is not null) written.Add("description");
        if (references.RelationshipType is not null || references.RelationshipId is not null) written.Add("relationshipRef");
        if (references.RecordModuleKey is not null || references.RecordId is not null || references.RecordLabel is not null) written.Add("recordRef");
        if (references.SourceType is not null || references.SourceId is not null || references.SourceEvidence is not null) written.Add("sourceRef");
        return Refusal(written.Where(fieldKey => !access.CanWrite(fieldKey)).ToList());
    }

    internal static TaskOperationError? GuardFieldWrite(RecordAccessAuthorization access, params string[] fieldKeys)
    {
        return Refusal(fieldKeys.Where(fieldKey => !access.CanWrite(fieldKey)).ToList());
    }

    private static TaskOperationError? Refusal(List<string> blocked) =>
        blocked.Count == 0
            ? null
            : new TaskOperationError(
                "ACCESS_DENIED",
                403,
                "Access denied",
                $"Field security does not permit writing: {string.Join(", ", blocked.Order(StringComparer.Ordinal))}.");
}

/// <summary>
/// Tasks capability and field restriction gate. Activity scope is inherited from every referenced
/// CRM record through ActivityRecordAccess; the actor is never treated as the record owner.
/// Free text remains unavailable under restrictive Tasks field policies.
/// </summary>
internal static class TaskActivitySecurity
{
    internal static bool IsReachable(TaskAccess access) =>
        !access.Authorization.FieldEnforcement.Any(entry => entry.Value != RecordFieldEnforcement.ReadWrite);
}

/// <summary>
/// The Tasks application boundary of the trusted authority chain: authenticated user -> requested
/// Workspace -> verified membership -> trusted CurrentWorkspace -> capability authorization ->
/// record scope -> field security -> Tasks use case.
///
/// <para>Everything beyond the capability check is decided by AccessControl through
/// <see cref="IRecordAccessEvaluator"/>. Tasks holds no scope rule and no field rule of its own.</para>
/// </summary>
internal sealed class TaskAuthorization(IRecordAccessEvaluator evaluator)
{
    internal const string ResourceKey = "tasks";

    /// <param name="representation">
    /// The representation the calling operation will return. It decides only whether a restrictive
    /// policy on a field this resource declares required can be honoured by omitting the value; it
    /// can never widen read or write access. Operations returning the full read model pass
    /// <see cref="RecordAccessRepresentation.Full"/>, which is the default.
    /// </param>
    internal async Task<TaskOperationResult<TaskAccess>> AuthorizeAsync(
        AccessRequirement requirement,
        TaskRequestMetadata metadata,
        CancellationToken cancellationToken,
        RecordAccessRepresentation? representation = null)
    {
        var authorization = await evaluator.AuthorizeResourceAsync(
            ResourceKey,
            requirement.Capability,
            TaskFieldSecurity.FieldKeys,
            representation ?? RecordAccessRepresentation.Full,
            new RecordAccessRequestContext(metadata.RequestId, metadata.CorrelationId),
            cancellationToken);

        if (authorization.TrustedWorkspace is not { } trusted)
        {
            return TaskOperationResult<TaskAccess>.Failure(
                authorization.Code == "WORKSPACE_MISMATCH" ? TaskErrors.WorkspaceMismatch() : TaskErrors.AccessDenied());
        }

        if (!authorization.IsAllowed)
            return TaskOperationResult<TaskAccess>.Failure(TaskErrors.AccessDenied());

        var unenforceable = TaskFieldSecurity.UnenforceablePolicy(authorization);
        if (unenforceable is not null)
            return TaskOperationResult<TaskAccess>.Failure(unenforceable);

        return TaskOperationResult<TaskAccess>.Success(new TaskAccess(trusted, authorization));
    }

    /// <summary>
    /// Enforces record scope against the Tasks-owned authoritative fact. The assignee is the member
    /// reference Tasks records for a task, and the already-implemented Tasks summary reader has
    /// treated it as the OWN-scope subject since B04; nothing else in the aggregate is a member
    /// owner, so nothing else is substituted for one. A task outside scope is reported as not found.
    /// </summary>
    internal async Task<TaskOperationError?> EnforceRecordAsync(
        TaskAccess access,
        TaskItem task,
        string enforcementPoint,
        TaskRequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        var decision = await evaluator.AuthorizeRecordAsync(
            access.Authorization,
            task.TaskId,
            RecordAccessFacts.Found(task.AssigneeId),
            enforcementPoint,
            new RecordAccessRequestContext(metadata.RequestId, metadata.CorrelationId),
            cancellationToken);
        return decision.IsAllowed ? null : TaskErrors.NotFound();
    }

    /// <summary>
    /// Authorizes the fields a command is about to write. It is deliberately separate from the
    /// record guard and is applied only on the new-execution path: record scope is current
    /// authorization and must gate a replay, whereas a replay performs no write at all and must not
    /// be refused for lacking permission to write what was already written.
    /// </summary>
    internal static TaskOperationError? EnforceFieldWrite(TaskAccess access, params string[] writtenFieldKeys) =>
        writtenFieldKeys.Length == 0
            ? null
            : TaskFieldSecurity.GuardFieldWrite(access.Authorization, writtenFieldKeys);
}
