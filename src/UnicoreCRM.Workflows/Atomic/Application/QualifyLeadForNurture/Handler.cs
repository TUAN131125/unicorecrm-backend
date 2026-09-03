using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Crm.Contacts.Contracts;
using UnicoreCRM.Crm.Leads.Contracts;
using UnicoreCRM.Operations.Tasks.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Workflows.Atomic.Contracts;
using UnicoreCRM.Workflows.Atomic.Domain;
using UnicoreCRM.Workflows.Atomic.Infrastructure.Persistence;

namespace UnicoreCRM.Workflows.Atomic.Application.QualifyLeadForNurture;

/// <summary>
/// The Lead communication restrictions the coordinator forwards to Contacts under the frozen consent
/// transfer. The coordinator carries them; it never reads, derives or interprets them.
/// </summary>
internal readonly record struct LeadCommunicationRestrictions(bool? DoNotCall, bool? DoNotEmail);

/// <summary>
/// A participant reported <b>transient contention</b> as an ordinary typed result rather than as an
/// exception. The coordinator raises this owner-neutral signal so its one bounded contention retry -
/// which is otherwise driven by exceptions - observes the condition and re-drives the attempt.
///
/// It carries no provider detail and no owner internals. The owner classified the condition; this
/// only records that the classification was <i>transient</i>, which is the sole fact the coordinator
/// is entitled to act on. A permanent duplicate, unresolvable or invalid outcome is never raised
/// here: those are caller verdicts and are answered, not retried.
/// </summary>
internal sealed class ParticipantContentionException(string participant)
    : Exception($"The {participant} participant reported transient contention.");

/// <summary>
/// The WF-10 NURTURE coordinator.
///
/// It is <b>deterministic convergent, not atomic</b>. The three owners commit in their own
/// owner-local transactions and no cross-DbContext transaction exists, so completion is durable
/// progress rather than one commit. The order is forced: Contact, then Task, then the Lead close.
/// Closing the Lead first would leave a committed Lead whose <c>relationshipRef</c> points at a
/// Contact that does not exist, violating the frozen lifecycle invariant; committing the Contact
/// first is the only order in which every individually committed state is independently valid.
///
/// Recovery is forward-only. A committed Contact or Task is never deleted as compensation - Contacts
/// exposes no delete surface, and destroying owner state to tidy up a coordinator failure is exactly
/// what the freeze prohibits. An interrupted attempt is resumed from the anchor, and each
/// participant's own idempotency guarantees a re-drive converges on the same Contact, the same Task
/// and one terminal Lead result.
///
/// Workflows owns no business state: it opens no foreign DbContext, assigns no Contact, Task or Lead
/// identity, and reaches every owner only through that owner's narrow participant contract.
/// </summary>
internal sealed class Handler(
    WorkflowsDbContext dbContext,
    ICurrentWorkspace currentWorkspace,
    ILeadQualificationParticipant leads,
    IContactQualificationParticipant contacts,
    ILeadQualificationTaskParticipant tasks,
    TimeProvider timeProvider) : ILeadNurtureQualificationWorkflow
{
    internal const string Workflow = "qualifyLeadForNurture";

    /// <summary>
    /// Concurrent duplicates contend on the same anchor row and on each participant's own
    /// serializable transaction, so one of them can be chosen as a deadlock victim. A victim
    /// committed nothing in the step it lost, and every completed step is already durable, so
    /// re-driving simply resumes from wherever the workflow actually got to. That is the same
    /// convergence a crashed coordinator relies on, applied to contention.
    ///
    /// The bound covers every transient condition identically, whether it arrived as a provider
    /// contention error raised inside this coordinator's own transaction or as a participant's typed
    /// transient outcome. Exhausting it answers with the admitted <c>INTERNAL_ERROR</c> (500) this
    /// operation already uses for exhausted contention - never with a caller-validation refusal,
    /// which would misreport a transient infrastructure condition as invalid input.
    /// </summary>
    private const int MaxAttempts = 3;

    public async Task<LeadNurtureQualificationResult> ExecuteAsync(
        LeadNurtureQualificationCommand command,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                return await ExecuteOnceAsync(command, cancellationToken);
            }
            catch (Exception exception) when (IsContention(exception) && attempt < MaxAttempts)
            {
                // Nothing this attempt was mid-way through committed. Drop the stale anchor from the
                // tracker so the retry re-reads durable state rather than its own lost view.
                dbContext.ChangeTracker.Clear();
            }
            catch (Exception exception) when (IsContention(exception))
            {
                return Error("INTERNAL_ERROR", 500);
            }
        }

        return Error("INTERNAL_ERROR", 500);
    }

    private async Task<LeadNurtureQualificationResult> ExecuteOnceAsync(
        LeadNurtureQualificationCommand command,
        CancellationToken cancellationToken)
    {
        var validation = Validate(command);
        if (validation is not null)
            return validation;

        if (!currentWorkspace.IsResolved)
            return Error("ACCESS_DENIED", 403);
        var trusted = currentWorkspace.Require();

        // Current Lead authorization gates every anchor read, including replay and conflict.
        // It deliberately excludes If-Match and lifecycle/profile preconditions: a completed
        // qualification has already closed the Lead, but an authorized caller may still replay it.
        var access = await leads.AuthorizeAsync(
            new LeadQualificationAccessQuery(command.LeadId, command.RequestId, command.CorrelationId),
            cancellationToken);
        if (!access.IsSuccess)
            return Error(access.ErrorCode!, access.ErrorStatus!.Value);
        if (!string.Equals(access.TrustedWorkspace!.WorkspaceId, trusted.WorkspaceId, StringComparison.Ordinal))
            return Error("WORKSPACE_MISMATCH", 403);

        var scopeKey = ScopeKey(trusted.WorkspaceId, command);
        var fingerprint = Fingerprint(command);

        var anchor = await dbContext.LeadQualificationAnchors
            .SingleOrDefaultAsync(item => item.ScopeKey == scopeKey, cancellationToken);

        // Legacy anchors did not record Task owner intent; it cannot be recovered from a hash or
        // from current owner data. Refuse rather than silently accept an unprovable replay.
        if (anchor is not null && anchor.IntentVersion != 1)
            return Error("INTERNAL_ERROR", 500);

        // After authorization, compare intent before any participant mutation, so replaying a key
        // with a changed intent can never resolve a second Contact or create a second Task.
        if (anchor is not null && !string.Equals(anchor.Fingerprint, fingerprint, StringComparison.Ordinal))
            return Error("IDEMPOTENCY_KEY_REUSED", 409) with { IdempotencyKey = command.IdempotencyKey };

        // A completed workflow is answered from the anchor, never from Lead state: after a
        // successful close the Lead is legitimately CLOSED and would fail its own precondition.
        if (anchor is { Stage: LeadQualificationStage.Completed })
            return Replayed(anchor);

        // The Lead gate mutates nothing and runs before any foreign owner commits, so an unknown,
        // foreign-Workspace, scope-denied, non-VERIFYING, profile-incomplete or stale Lead can never
        // leave a Contact or a Task behind. It also supplies the Lead owner the later steps need.
        var prepared = await leads.PrepareAsync(
            new LeadQualificationPrepareCommand(
                command.LeadId,
                command.RequestId,
                command.CorrelationId,
                command.ExpectedVersion),
            cancellationToken);

        if (!prepared.IsSuccess)
        {
            // The one case where a refusing gate is not the answer: both participants already
            // committed, so the Lead is refusing precisely because its close succeeded and only the
            // anchor update was lost. The Lead's own idempotency record settles it below.
            if (anchor is not { ContactId: not null, TaskId: not null })
            {
                return Error(prepared.ErrorCode!, prepared.ErrorStatus!.Value, prepared.FieldErrors)
                    with { ExpectedVersion = command.ExpectedVersion, CurrentVersion = prepared.Version };
            }
        }
        else if (!string.Equals(prepared.TrustedWorkspace!.WorkspaceId, trusted.WorkspaceId, StringComparison.Ordinal))
        {
            return Error("WORKSPACE_MISMATCH", 403);
        }

        anchor ??= await StartAsync(scopeKey, trusted, command, fingerprint, prepared.OwnerId!, cancellationToken);

        // A concurrent request carrying the same key may have won the insert and already advanced.
        if (!string.Equals(anchor.Fingerprint, fingerprint, StringComparison.Ordinal))
            return Error("IDEMPOTENCY_KEY_REUSED", 409) with { IdempotencyKey = command.IdempotencyKey };
        if (anchor.Stage == LeadQualificationStage.Completed)
            return Replayed(anchor);

        return await ConvergeAsync(
            command,
            anchor,
            trusted,
            prepared.OwnerId,
            new LeadCommunicationRestrictions(prepared.DoNotCall, prepared.DoNotEmail),
            cancellationToken);
    }

    private async Task<LeadNurtureQualificationResult> ConvergeAsync(
        LeadNurtureQualificationCommand command,
        LeadQualificationAnchor anchor,
        TrustedWorkspaceContext trusted,
        string? leadOwnerId,
        LeadCommunicationRestrictions restrictions,
        CancellationToken cancellationToken)
    {
        if (anchor.ParticipantMemberId is null || anchor.TaskAssigneeId is null || anchor.CorrelationId is null)
            return Error("INTERNAL_ERROR", 500);

        // ---- 1. Contact ------------------------------------------------------------------
        if (anchor.ContactId is null)
        {
            if (leadOwnerId is null)
                return Error("INTERNAL_ERROR", 500);

            var resolution = await contacts.ResolveAsync(
                new ResolveQualificationContactCommand(
                    trusted,
                    command.Contact.Mode == LeadNurtureRelationshipMode.New
                        ? ContactQualificationMode.New
                        : ContactQualificationMode.Existing,
                    command.Contact.SelectedContactId,
                    new ContactQualificationInput(
                        command.Contact.DisplayName ?? string.Empty,
                        command.Contact.Email,
                        command.Contact.Phone,
                        command.Contact.Title),
                    // The Lead owner becomes the Contact's record-owner fact, so the conversion does
                    // not produce a Contact invisible to every OWN-scoped member.
                    leadOwnerId,
                    // The conversion key is the workflow identity, so a re-drive adopts the same
                    // Contact instead of creating a second one, even if this anchor update is lost.
                    anchor.ScopeKey,
                    command.RequestId,
                    command.CorrelationId,
                    // The frozen consent transfer. These are Leads-supplied facts forwarded
                    // unchanged; the coordinator never interprets them, and Contacts writes only a
                    // restriction.
                    restrictions.DoNotCall,
                    restrictions.DoNotEmail),
                cancellationToken);

            if (!resolution.IsSuccess)
            {
                // Transient contention is not a caller verdict. Contacts exhausted its own bounded
                // deadlock retry without committing anything and said so with a typed outcome that
                // is deliberately distinct from every permanent relationship refusal. Collapsing it
                // into the relationship-invalid 422 would tell a caller their valid command was
                // wrong and stop them retrying it, so it is re-driven by this coordinator's own
                // bounded retry instead. Ownership is intact: Contacts classified the condition and
                // Workflows recognises only that the classification was transient.
                if (resolution.Rejection == ContactQualificationRejection.ConcurrentConflict)
                    throw new ParticipantContentionException("Contacts");

                // Every Contacts rejection collapses into the one admitted relationship error, so the
                // response never discloses which Contact matched, how many did, or any of its fields.
                //
                // The duplicate address carries the field pointer the duplicate policy freezes
                // (DEC-LEAD-CONTACT-DUPLICATE-POLICY 9.4). It reveals nothing the frozen rule does not
                // already: on a NEW request the caller learns duplicate-versus-created from the
                // refusal itself, and the pointer only names which of their own inputs caused it.
                // Every other rejection stays pointer-less, so an unresolvable EXISTING identifier
                // remains indistinguishable from a record the caller may not read.
                return resolution.Rejection == ContactQualificationRejection.DuplicateEmail
                    ? Error("LEAD_QUALIFICATION_RELATIONSHIP_INVALID", 422, DuplicateEmailField)
                    : Error("LEAD_QUALIFICATION_RELATIONSHIP_INVALID", 422);
            }

            if (resolution.ContactId is null || resolution.ContactVersion is null || resolution.DisplayName is null)
                return Error("INTERNAL_ERROR", 500);
            anchor.RecordContact(resolution.ContactId, resolution.ContactVersion.Value,
                resolution.WasCreated, resolution.DisplayName, timeProvider.GetUtcNow());
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        // ---- 2. Follow-up Task -----------------------------------------------------------
        if (anchor.TaskId is null)
        {
            if (leadOwnerId is null)
                return Error("INTERNAL_ERROR", 500);

            var created = await tasks.CreateNurtureFollowUpAsync(
                new LeadNurtureTaskCommand(
                    anchor.LeadId,
                    anchor.ContactId!,
                    anchor.TaskAssigneeId,
                    command.RevisitAt,
                    command.Reason,
                    command.Note,
                    command.RequestId,
                    command.CorrelationId,
                    // Derived from the workflow identity, so Tasks' own idempotency record converges
                    // on one Task across every re-drive.
                    TaskIdempotencyKey(anchor.ScopeKey),
                    anchor.ParticipantMemberId),
                cancellationToken);

            if (!created.IsSuccess)
            {
                // A missing tasks.create is exactly what the admitted downstream-capability error
                // exists for. The Contact has already committed and is deliberately not
                // compensated; granting the capability and retrying converges on the same Contact.
                return created.ErrorStatus == 403
                    ? Error("LEAD_QUALIFICATION_DOWNSTREAM_CAPABILITY_REQUIRED", 403)
                    : Error(created.ErrorCode!, created.ErrorStatus!.Value, created.FieldErrors);
            }

            if (created.TaskId is null || created.TaskVersion is null)
                return Error("INTERNAL_ERROR", 500);
            anchor.RecordTask(created.TaskId, created.TaskVersion.Value, timeProvider.GetUtcNow());
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        // ---- 3. Terminal Lead close ------------------------------------------------------
        var closure = await leads.CloseForNurtureAsync(
            new LeadQualificationCloseCommand(
                anchor.LeadId,
                anchor.ContactId!,
                command.RequestId,
                anchor.CorrelationId,
                command.IdempotencyKey,
                command.ExpectedVersion,
                anchor.ParticipantMemberId),
            cancellationToken);

        if (!closure.IsSuccess)
        {
            return Error(closure.ErrorCode!, closure.ErrorStatus!.Value, closure.FieldErrors)
                with { ExpectedVersion = closure.ExpectedVersion, CurrentVersion = closure.CurrentVersion };
        }

        if (anchor.ContactVersion is null || anchor.ContactWasCreated is null || anchor.ContactDisplayName is null
            || anchor.TaskVersion is null || closure.CommandId is null || closure.OccurredAt is null
            || closure.CorrelationId is null || closure.Outcome is null
            || closure.EmittedEventIds is null || closure.AuditEvidenceIds is null)
            return Error("INTERNAL_ERROR", 500);

        var response = ComposeResponse(anchor, closure);
        anchor.Complete(closure.Version!.Value, JsonSerializer.Serialize(response, ResponseJson), timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);

        return new LeadNurtureQualificationResult(
            true,
            // Leads decides COMMITTED versus REPLAYED: it holds the authoritative idempotency record
            // for the terminal transition.
            closure.Outcome,
            anchor.LeadId,
            closure.Version,
            anchor.ContactId,
            anchor.TaskId,
            "NURTURE",
            null,
            null,
            null,
            response);
    }

    /// <summary>
    /// Builds the adopted wire response from what the owners actually reported. Nothing is invented:
    /// the command identity, instant and evidence identifiers come from the Leads command record, the
    /// Contact identity and display name from Contacts, and the Task identity and version from Tasks.
    /// </summary>
    private static LeadQualificationWorkflowResponse ComposeResponse(
        LeadQualificationAnchor anchor,
        LeadQualificationClosure closure)
    {
        var createdResources = new List<LeadQualificationCreatedResource>();
        // Creation belongs to the workflow, not to whichever retry received the acknowledgment.
        if (anchor.ContactWasCreated == true)
            createdResources.Add(new("CONTACT", anchor.ContactId!, anchor.ContactVersion!.Value));
        createdResources.Add(new("TASK", anchor.TaskId!, anchor.TaskVersion!.Value));

        return new LeadQualificationWorkflowResponse(
            closure.CommandId!,
            closure.CorrelationId!,
            anchor.LeadId,
            "Lead",
            closure.Version!.Value,
            closure.OccurredAt!,
            closure.Outcome!,
            new LeadQualificationWorkflowResult(
                anchor.LeadId,
                closure.Version.Value,
                "NURTURE",
                new LeadQualificationResolvedRelationship(
                    new QualificationRelationshipRef("CONTACT", anchor.ContactId!),
                    anchor.ContactDisplayName!)
                {
                    ContactId = anchor.ContactId
                },
                createdResources)
            {
                ContactId = anchor.ContactId,
                TaskId = anchor.TaskId
            },
            [],
            closure.EmittedEventIds!,
            closure.AuditEvidenceIds!);
    }

    /// <summary>
    /// Inserts the anchor, or adopts the row a concurrent request inserted first. The primary key is
    /// the workflow identity, so exactly one execution ever starts per Idempotency-Key.
    /// </summary>
    private async Task<LeadQualificationAnchor> StartAsync(
        string scopeKey,
        TrustedWorkspaceContext trusted,
        LeadNurtureQualificationCommand command,
        string fingerprint,
        string leadOwnerId,
        CancellationToken cancellationToken)
    {
        var anchor = new LeadQualificationAnchor(
            scopeKey,
            trusted.WorkspaceId,
            Workflow,
            command.LeadId,
            command.IdempotencyKey,
            fingerprint,
            command.ExpectedVersion,
            trusted.MemberId,
            command.TaskOwnerId ?? leadOwnerId,
            command.CorrelationId,
            timeProvider.GetUtcNow());
        dbContext.LeadQualificationAnchors.Add(anchor);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return anchor;
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            return await dbContext.LeadQualificationAnchors
                .SingleAsync(item => item.ScopeKey == scopeKey, cancellationToken);
        }
    }

    /// <summary>
    /// The whole adopted request contract, checked before anything else runs. It is deliberately the
    /// first statement of the execution: every later stage either commits owner state or depends on
    /// state a refused request must never reach, and the workflow has no compensation to undo a
    /// Contact created for a body that was never valid.
    /// </summary>
    private static LeadNurtureQualificationResult? Validate(LeadNurtureQualificationCommand command)
    {
        var fields = NurtureRequestValidation.Validate(command);
        return fields.Count == 0 ? null : Error("VALIDATION_FAILED", 422, fields);
    }

    /// <summary>
    /// A replay returns the stored response verbatim with its outcome relabelled REPLAYED, so a
    /// caller cannot observe a response recomposed from partial state.
    /// </summary>
    private static LeadNurtureQualificationResult Replayed(LeadQualificationAnchor anchor)
    {
        LeadQualificationWorkflowResponse? stored = null;
        if (anchor.ResponseJson is { Length: > 0 } json)
        {
            stored = JsonSerializer.Deserialize<LeadQualificationWorkflowResponse>(json, ResponseJson) is { } value
                ? value with { Outcome = "REPLAYED" }
                : null;
        }

        if (stored is null)
            return Error("INTERNAL_ERROR", 500);

        return new LeadNurtureQualificationResult(
            true, "REPLAYED", anchor.LeadId, anchor.LeadVersion, anchor.ContactId, anchor.TaskId,
            "NURTURE", null, null, null, stored);
    }

    private static readonly JsonSerializerOptions ResponseJson = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The exact pointer frozen for a Contacts-owned duplicate refusal. The message names the caller's
    /// own recovery - resubmit as EXISTING with an identifier they resolved through their own reads -
    /// and asserts nothing about the matched record.
    /// </summary>
    private static readonly Dictionary<string, string[]> DuplicateEmailField = new(StringComparer.Ordinal)
    {
        ["relationship.contact.email"] =
            ["A contact with this email address already exists in this workspace. Qualify with mode EXISTING instead."]
    };

    private static LeadNurtureQualificationResult Error(
        string code,
        int status,
        IReadOnlyDictionary<string, string[]>? fields = null) =>
        new(false, null, null, null, null, null, null, code, status, fields);

    /// <summary>
    /// 1205 deadlock victim and 1222 lock request timeout. Both mean the losing statement committed
    /// nothing, so the attempt can be re-driven safely. An optimistic-concurrency failure on the
    /// anchor means another coordinator advanced it first, which is the same situation. A
    /// participant that classified its own contention and reported it as a typed transient outcome
    /// is the third, and is the only route by which an owner's internal contention reaches here -
    /// Workflows never inspects a provider error owned by another module.
    /// </summary>
    private static bool IsContention(Exception exception) => exception switch
    {
        ParticipantContentionException => true,
        DbUpdateConcurrencyException => true,
        SqlException sql => sql.Number is 1205 or 1222,
        _ => exception.InnerException is not null && IsContention(exception.InnerException)
    };

    private static string TaskIdempotencyKey(string scopeKey) => $"wf-nurture-task-{scopeKey}";

    private static string ScopeKey(string workspaceId, LeadNurtureQualificationCommand command) =>
        Hash($"{workspaceId}\n{Workflow}\n{command.LeadId}\n{command.IdempotencyKey}");

    private static string Fingerprint(LeadNurtureQualificationCommand command) =>
        Hash(JsonSerializer.Serialize(new
        {
            Operation = Workflow,
            command.LeadId,
            command.TaskOwnerId,
            command.RevisitAt,
            command.Reason,
            command.Note,
            Mode = command.Contact.Mode.ToString(),
            command.Contact.SelectedContactId,
            command.Contact.DisplayName,
            command.Contact.Email,
            command.Contact.Phone,
            command.Contact.Title
        }));

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..48];
}
