using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using UnicoreCRM.Crm.Contacts.Application.Common;
using UnicoreCRM.Crm.Contacts.Contracts;
using UnicoreCRM.Crm.Contacts.Domain;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Contacts.Application.ResolveQualificationContact;

/// <summary>
/// The Contacts-owned Lead qualification participant frozen by
/// <c>DEC-LEAD-CONTACT-QUALIFICATION-CLOSURE</c> and <c>DEC-LEAD-CONTACT-DUPLICATE-POLICY</c>.
///
/// It resolves exactly two caller-declared modes and never converts one into the other. It exposes
/// no HTTP surface, opens no foreign DbContext, and returns only the coordinator's minimum: the
/// decision plus the authoritative Contact identity.
/// </summary>
internal sealed partial class Handler(
    ContactAuthorization authorization,
    IContactsPersistence persistence,
    ICurrentWorkspace currentWorkspace,
    TimeProvider timeProvider) : IContactQualificationParticipant
{
    private const string Operation = "resolveQualificationContact";
    private const string CreatedEventType = "CONTACT_CREATED";

    /// <summary>The Contacts-owned initial status. Every other admitted value asserts a fact this owner cannot prove.</summary>
    private const string InitialStatus = "active";

    /// <summary>
    /// One retry only. A SERIALIZABLE loser rolls back having committed nothing; on the re-drive the
    /// winner's row is committed, so the duplicate guard sees it and this call rejects cleanly
    /// instead of surfacing a deadlock. A deadlock does not by itself prove a duplicate - key-range
    /// locks cover gaps, so adjacent keys can contend - which is exactly why the decision is left to
    /// the re-drive rather than inferred from the exception.
    /// </summary>
    private const int MaxCreateAttempts = 2;

    public async Task<ResolveQualificationContactResult> ResolveAsync(
        ResolveQualificationContactCommand command,
        CancellationToken cancellationToken)
    {
        if (!IsTrustedWorkspaceConsistent(command.TrustedWorkspace))
            return Reject(ContactQualificationRejection.InvalidInput);

        return command.Mode switch
        {
            ContactQualificationMode.Existing => await LinkExistingAsync(command, cancellationToken),
            ContactQualificationMode.New => await CreateNewAsync(command, cancellationToken),
            _ => Reject(ContactQualificationRejection.InvalidInput)
        };
    }

    /// <summary>
    /// Fail closed if an ambient trusted Workspace is resolved and disagrees with the one the
    /// coordinator supplied. The coordinator is trusted to derive the context, not to relabel it.
    /// </summary>
    private bool IsTrustedWorkspaceConsistent(TrustedWorkspaceContext supplied) =>
        string.IsNullOrWhiteSpace(supplied.WorkspaceId) is false
        && (!currentWorkspace.IsResolved
            || string.Equals(currentWorkspace.Require().WorkspaceId, supplied.WorkspaceId, StringComparison.Ordinal));

    /// <summary>
    /// EXISTING. Validates and returns; it never writes. The supplied <c>Contact</c> object is
    /// ignored for identity and is deliberately not applied, because applying it would be the
    /// BLOCKED <c>updateContact</c> mutation under another name.
    /// </summary>
    private async Task<ResolveQualificationContactResult> LinkExistingAsync(
        ResolveQualificationContactCommand command,
        CancellationToken cancellationToken)
    {
        if (command.SelectedContactId is not { Length: > 0 } selectedContactId
            || !EntityIdPattern().IsMatch(selectedContactId))
        {
            return Reject(ContactQualificationRejection.InvalidInput);
        }

        var metadata = new ContactRequestMetadata(command.RequestId, command.CorrelationId);

        // contacts.read is required here and evaluated through the canonical evaluator at this
        // owner's boundary. Without it a caller could probe Contact existence, or attach a Lead to a
        // record outside their own record scope. Every failure below collapses into one
        // indistinguishable rejection so the result is never an existence oracle.
        var access = await authorization.AuthorizeAsync(metadata, cancellationToken);
        if (!access.IsSuccess
            || !string.Equals(access.Value!.Trusted.WorkspaceId, command.TrustedWorkspace.WorkspaceId, StringComparison.Ordinal))
        {
            return Reject(ContactQualificationRejection.ContactNotResolvable);
        }

        var contact = await persistence.ReadContactAsync(
            command.TrustedWorkspace.WorkspaceId,
            selectedContactId,
            cancellationToken);
        if (contact is null)
            return Reject(ContactQualificationRejection.ContactNotResolvable);

        var denied = await authorization.EnforceRecordAsync(access.Value, contact, Operation, metadata, cancellationToken);
        if (denied is not null)
            return Reject(ContactQualificationRejection.ContactNotResolvable);

        // No mutation, so no command audit and no outbox message. No read audit either: this is an
        // internal identity validation, not a public Contact disclosure, and writing a getContact
        // read record would attribute a disclosure that did not happen.
        return new ResolveQualificationContactResult(
            ContactQualificationDecision.Linked,
            contact.ContactId,
            contact.Version);
    }

    /// <summary>
    /// NEW. The duplicate guard and the insert run inside one Contacts-owned SERIALIZABLE
    /// transaction, so two concurrent conversions of the same address cannot both commit.
    /// </summary>
    private async Task<ResolveQualificationContactResult> CreateNewAsync(
        ResolveQualificationContactCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Contact is not { } input
            || string.IsNullOrWhiteSpace(input.DisplayName)
            || input.DisplayName.Trim().Length > 200
            || string.IsNullOrWhiteSpace(command.OwnerId)
            || string.IsNullOrWhiteSpace(command.ConversionKey))
        {
            return Reject(ContactQualificationRejection.InvalidInput);
        }

        var scopeKey = ConversionScopeKey(command.TrustedWorkspace.WorkspaceId, command.ConversionKey);
        var normalizedEmail = ContactEmailIdentity.Normalize(input.Email);

        for (var attempt = 1; attempt <= MaxCreateAttempts; attempt++)
        {
            try
            {
                return await AttemptCreateAsync(command, input, scopeKey, normalizedEmail, cancellationToken);
            }
            catch (SqlException exception) when (IsConcurrencyFailure(exception) && attempt < MaxCreateAttempts)
            {
                // Nothing was committed by this attempt. Re-drive so the guard can observe whatever
                // the winning transaction committed and reach a determinate decision.
            }
            catch (SqlException exception) when (IsConcurrencyFailure(exception))
            {
                return Reject(ContactQualificationRejection.ConcurrentConflict);
            }
        }

        return Reject(ContactQualificationRejection.ConcurrentConflict);
    }

    private async Task<ResolveQualificationContactResult> AttemptCreateAsync(
        ResolveQualificationContactCommand command,
        ContactQualificationInput input,
        string scopeKey,
        string? normalizedEmail,
        CancellationToken cancellationToken)
    {
        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);

        // Replay precedes the guard: a re-drive of a conversion this owner already completed must
        // return the same Contact, not be rejected as a duplicate of itself.
        var existingConversion = await persistence.FindConversionAsync(scopeKey, cancellationToken);
        if (existingConversion is not null)
        {
            var replayed = await persistence.ReadContactAsync(
                command.TrustedWorkspace.WorkspaceId,
                existingConversion.ContactId,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return replayed is null
                ? Reject(ContactQualificationRejection.ContactNotResolvable)
                : new ResolveQualificationContactResult(
                    ContactQualificationDecision.Replayed,
                    replayed.ContactId,
                    replayed.Version);
        }

        if (normalizedEmail is not null
            && await persistence.AnyContactWithNormalizedEmailAsync(
                command.TrustedWorkspace.WorkspaceId,
                normalizedEmail,
                cancellationToken))
        {
            // Fail closed. NEW is never silently converted into a link, and the matched Contact is
            // never identified, counted or described - one match and several are indistinguishable.
            return Reject(ContactQualificationRejection.DuplicateEmail);
        }

        var now = timeProvider.GetUtcNow();
        var fullName = input.DisplayName.Trim();
        var contact = new Contact(
            command.TrustedWorkspace.WorkspaceId,
            command.OwnerId,
            fullName,
            InitialStatus,
            new ContactProfile
            {
                // The frozen landing fields. Nothing else is written: no Lead profile fact is copied,
                // and consent and the do-not-contact flags are deliberately not transferred.
                WorkEmail = Trimmed(input.Email),
                MobilePhone = Trimmed(input.Phone),
                JobTitle = Trimmed(input.Title),
                // A declared read-only projection of fullName, not an independent fact.
                DisplayName = fullName
            },
            now);

        persistence.AddContact(contact);
        persistence.AddConversion(new ContactConversionRecord(
            scopeKey,
            command.TrustedWorkspace.WorkspaceId,
            command.ConversionKey,
            contact.ContactId,
            now));
        persistence.AddAudit(new ContactAuditRecord(
            Operation,
            command.TrustedWorkspace.WorkspaceId,
            command.TrustedWorkspace.MemberId,
            contact.ContactId,
            command.RequestId,
            command.CorrelationId,
            "COMMITTED",
            contact.Version,
            now));
        persistence.AddOutbox(new ContactOutboxMessage(
            CreatedEventType,
            contact.ContactId,
            command.TrustedWorkspace.WorkspaceId,
            command.CorrelationId,
            JsonSerializer.Serialize(new
            {
                contactId = contact.ContactId,
                workspaceId = contact.WorkspaceId,
                version = contact.Version
            }),
            now));

        await persistence.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ResolveQualificationContactResult(
            ContactQualificationDecision.Created,
            contact.ContactId,
            contact.Version);
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ResolveQualificationContactResult Reject(ContactQualificationRejection rejection) =>
        new(ContactQualificationDecision.Rejected, null, null, rejection);

    // 1205 deadlock victim; 1222 lock request timeout. Both mean this attempt committed nothing.
    private static bool IsConcurrencyFailure(SqlException exception) =>
        exception.Number is 1205 or 1222;

    private static string ConversionScopeKey(string workspaceId, string conversionKey)
    {
        var payload = $"{workspaceId}{conversionKey}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))[..48];
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityIdPattern();
}
