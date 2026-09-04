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
    private const int MaxResolutionAttempts = 2;

    public async Task<ResolveQualificationContactResult> ResolveAsync(
        ResolveQualificationContactCommand command,
        CancellationToken cancellationToken)
    {
        if (!IsTrustedWorkspaceConsistent(command.TrustedWorkspace))
            return Reject(ContactQualificationRejection.InvalidInput);

        for (var attempt = 1; attempt <= MaxResolutionAttempts; attempt++)
        {
            try
            {
                return command.Mode switch
                {
                    ContactQualificationMode.Existing => await LinkExistingAsync(command, cancellationToken),
                    ContactQualificationMode.New => await CreateNewAsync(command, cancellationToken),
                    _ => Reject(ContactQualificationRejection.InvalidInput)
                };
            }
            catch (Exception exception) when (IsConcurrencyFailure(exception) && attempt < MaxResolutionAttempts)
            {
                // The owner transaction has rolled back and discarded its tracked writes. EF may
                // wrap a deadlock raised during SaveChanges, so classify the underlying SQL error.
            }
            catch (Exception exception) when (IsConcurrencyFailure(exception))
            {
                return Reject(ContactQualificationRejection.ConcurrentConflict);
            }
        }
        return Reject(ContactQualificationRejection.ConcurrentConflict);
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
    /// EXISTING. Validates and records a resolution receipt; it never mutates the Contact. The supplied <c>Contact</c> object is
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

        if (string.IsNullOrWhiteSpace(command.ConversionKey))
            return Reject(ContactQualificationRejection.InvalidInput);

        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);
        var scopeKey = ConversionScopeKey(command.TrustedWorkspace.WorkspaceId, command.ConversionKey);
        var receipt = await persistence.FindConversionAsync(scopeKey, cancellationToken);

        var contact = await persistence.ReadContactAsync(
            command.TrustedWorkspace.WorkspaceId,
            selectedContactId,
            cancellationToken);
        if (contact is null)
            return Reject(ContactQualificationRejection.ContactNotResolvable);

        var denied = await authorization.EnforceRecordAsync(access.Value, contact, Operation, metadata, cancellationToken);
        if (denied is not null)
            return Reject(ContactQualificationRejection.ContactNotResolvable);

        if (receipt is not null)
        {
            var replay = Replay(receipt, wasCreated: false);
            return replay.ContactId == selectedContactId ? replay : Reject(ContactQualificationRejection.InvalidInput);
        }

        // A durable resolution receipt preserves the exact owner-returned name/version if the
        // coordinator loses this acknowledgment. It changes no Contact, command audit or outbox.
        var result = new ResolveQualificationContactResult(
            ContactQualificationDecision.Linked,
            contact.ContactId,
            contact.Version,
            DisplayName: contact.FullName);
        persistence.AddConversion(new ContactConversionRecord(scopeKey, command.TrustedWorkspace.WorkspaceId,
            command.ConversionKey, contact.ContactId, JsonSerializer.Serialize(result), timeProvider.GetUtcNow()));
        await persistence.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    /// <summary>
    /// NEW. The duplicate guard and the insert run inside one Contacts-owned SERIALIZABLE
    /// transaction, so two concurrent conversions of the same address cannot both commit.
    /// </summary>
    private async Task<ResolveQualificationContactResult> CreateNewAsync(
        ResolveQualificationContactCommand command,
        CancellationToken cancellationToken)
    {
        // 200 is the Contact canonical name bound frozen by DEC-LEAD-CONTACT-NAME-BOUND: it is what
        // ContactDocument.fullName declares and what this owner's column holds. The coordinator now
        // enforces the same bound on the qualification input, so reaching this branch means a caller
        // bypassed the public route; it stays as this owner's own last word on its own aggregate.
        if (command.Contact is not { } input
            || string.IsNullOrWhiteSpace(input.DisplayName)
            || input.DisplayName.Trim().Length > ContactNameBound.MaxLength
            || string.IsNullOrWhiteSpace(command.OwnerId)
            || string.IsNullOrWhiteSpace(command.ConversionKey))
        {
            return Reject(ContactQualificationRejection.InvalidInput);
        }

        var scopeKey = ConversionScopeKey(command.TrustedWorkspace.WorkspaceId, command.ConversionKey);
        var normalizedEmail = ContactEmailIdentity.Normalize(input.Email);

        return await AttemptCreateAsync(command, input, scopeKey, normalizedEmail, cancellationToken);
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
            await transaction.CommitAsync(cancellationToken);
            return Replay(existingConversion, wasCreated: true);
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
                // The frozen consent transfer: restriction-only and true-only. A false or absent
                // Lead flag omits the field, because writing false would assert an affirmative
                // permission that nobody granted. Nothing else crosses: no consent ledger, no
                // lawfulBasis, no preferredChannel, no SMS or Zalo restriction - the Lead carries no
                // such value to transfer.
                DoNotCall = Restriction(command.DoNotCall),
                DoNotEmail = Restriction(command.DoNotEmail),
                // A declared read-only projection of fullName, not an independent fact.
                DisplayName = fullName
            },
            now);

        persistence.AddContact(contact);
        var result = new ResolveQualificationContactResult(
            ContactQualificationDecision.Created, contact.ContactId, contact.Version,
            DisplayName: contact.FullName, WasCreated: true);
        persistence.AddConversion(new ContactConversionRecord(
            scopeKey,
            command.TrustedWorkspace.WorkspaceId,
            command.ConversionKey,
            contact.ContactId,
            JsonSerializer.Serialize(result),
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

        return result;
    }

    private static ResolveQualificationContactResult Replay(ContactConversionRecord receipt, bool wasCreated)
    {
        // Legacy receipts lack the original name/version. Never fabricate them from current rows.
        var original = receipt.ResultJson is null ? null
            : JsonSerializer.Deserialize<ResolveQualificationContactResult>(receipt.ResultJson);
        return original is not { IsSuccess: true, ContactVersion: not null, DisplayName: not null }
            || original.ContactId != receipt.ContactId || original.WasCreated != wasCreated
            ? Reject(ContactQualificationRejection.ContactNotResolvable)
            : original with { Decision = wasCreated ? ContactQualificationDecision.Replayed : ContactQualificationDecision.Linked };
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Only a restriction survives. Unknown and "not restricted" are both unset.</summary>
    private static bool? Restriction(bool? value) => value == true ? true : null;

    private static ResolveQualificationContactResult Reject(ContactQualificationRejection rejection) =>
        new(ContactQualificationDecision.Rejected, null, null, rejection);

    // 1205 deadlock victim; 1222 lock request timeout. Both mean this attempt committed nothing.
    private static bool IsConcurrencyFailure(Exception exception) => exception is SqlException sql
        ? sql.Number is 1205 or 1222
        : exception.InnerException is not null && IsConcurrencyFailure(exception.InnerException);

    private static string ConversionScopeKey(string workspaceId, string conversionKey)
    {
        var payload = $"{workspaceId}{conversionKey}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))[..48];
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityIdPattern();
}
