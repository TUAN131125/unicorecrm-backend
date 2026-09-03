using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Contacts.Contracts;

/// <summary>
/// The closed relationship-resolution intent declared by the caller, mirroring
/// <c>LeadQualificationRelationshipMode</c>. The decision is caller-declared and backend-validated,
/// never backend-discovered: <see cref="New"/> is never silently resolved to <see cref="Existing"/>,
/// and <see cref="Existing"/> never falls back to creating.
/// </summary>
public enum ContactQualificationMode
{
    Existing,
    New
}

public enum ContactQualificationDecision
{
    /// <summary>An existing Contact was validated and returned. Nothing was mutated.</summary>
    Linked,

    /// <summary>A Contact was created and committed by this owner.</summary>
    Created,

    /// <summary>This conversion key already produced a Contact; the same identity is returned.</summary>
    Replayed,

    /// <summary>Nothing was committed. See <see cref="ResolveQualificationContactResult.Rejection"/>.</summary>
    Rejected
}

/// <summary>
/// Diagnostic detail for a <see cref="ContactQualificationDecision.Rejected"/> outcome.
///
/// It exists for this owner's audit and for coordinator logging. It MUST NOT be projected onto the
/// wire in any form: the duplicate guard deliberately sees Contacts outside the caller's record
/// scope, so distinguishing "this address already exists" from "that identifier is not resolvable"
/// would leak the existence of records the caller cannot read. Every value maps to the single
/// admitted public error <c>LEAD_QUALIFICATION_RELATIONSHIP_INVALID</c>.
/// </summary>
public enum ContactQualificationRejection
{
    None,

    /// <summary>Unknown, foreign-Workspace or record-scope-denied <c>SelectedContactId</c>. All three are indistinguishable.</summary>
    ContactNotResolvable,

    /// <summary>At least one Contact in the trusted Workspace already carries this normalized address.</summary>
    DuplicateEmail,

    /// <summary>The intent is structurally invalid for the declared mode.</summary>
    InvalidInput,

    /// <summary>Two concurrent conversions contended and this one did not converge. Retryable.</summary>
    ConcurrentConflict
}

/// <summary>
/// The Contact facts the coordinator passes through from <c>LeadQualificationContactInput</c>. They
/// are caller-supplied Contact content; this owner never reads the Lead profile to enrich or
/// backfill them, and <c>Email</c> is never a resolution key.
/// </summary>
public sealed record ContactQualificationInput(
    string DisplayName,
    string? Email,
    string? Phone,
    string? Title);

/// <summary>
/// The narrow internal boundary the Lead Qualification workflow calls. It is not public HTTP: it is
/// not <c>createContact</c>, which remains BLOCKED, and it widens no public Contacts surface.
/// </summary>
/// <param name="TrustedWorkspace">Server-derived trusted context. Never caller input.</param>
/// <param name="SelectedContactId">Required for <see cref="ContactQualificationMode.Existing"/>.</param>
/// <param name="Contact">
/// Required for <see cref="ContactQualificationMode.New"/>. For <see cref="ContactQualificationMode.Existing"/>
/// the wire schema still requires the object, but it is ignored for identity and is never applied as
/// an update - that is what keeps this boundary clear of the BLOCKED <c>updateContact</c> surface.
/// </param>
/// <param name="OwnerId">
/// The Lead's owner, becoming the Contact's AccessControl record-owner fact. It is a record-access
/// fact, not a profile fact: a null owner would leave the new Contact outside every OWN scope,
/// including that of the member who just qualified the Lead.
/// </param>
/// <param name="ConversionKey">
/// The coordinator-supplied key that makes this owner's replay deterministic.
/// </param>
public sealed record ResolveQualificationContactCommand(
    TrustedWorkspaceContext TrustedWorkspace,
    ContactQualificationMode Mode,
    string? SelectedContactId,
    ContactQualificationInput? Contact,
    string OwnerId,
    string ConversionKey,
    string RequestId,
    string CorrelationId);

/// <summary>
/// The minimum the coordinator needs: what happened, and the authoritative Contact identity.
/// No Contact field value, no match count and no matched identifier is ever returned for a rejection.
/// </summary>
public sealed record ResolveQualificationContactResult(
    ContactQualificationDecision Decision,
    string? ContactId,
    long? ContactVersion,
    ContactQualificationRejection Rejection = ContactQualificationRejection.None)
{
    public bool IsSuccess => Decision != ContactQualificationDecision.Rejected;
}

public interface IContactQualificationParticipant
{
    Task<ResolveQualificationContactResult> ResolveAsync(
        ResolveQualificationContactCommand command,
        CancellationToken cancellationToken);
}
