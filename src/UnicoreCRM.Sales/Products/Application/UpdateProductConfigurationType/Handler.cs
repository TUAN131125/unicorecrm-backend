using System.Text.Json;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Products.Application.Common;
using UnicoreCRM.Sales.Products.Contracts;
using UnicoreCRM.Sales.Products.Domain;

namespace UnicoreCRM.Sales.Products.Application.UpdateProductConfigurationType;

internal sealed record Command(
    string TypeId,
    UpdateProductConfigurationTypeRequest Request,
    ProductCommandMetadata Metadata);

/// <summary>
/// Sets the effective status of one of the nine always-existing Workspace ProductConfigurationTypes.
///
/// <para>Model B governs the whole use case: the resource always exists, the request names an
/// effective status, and whether the implementation writes, updates or removes an override row is
/// invisible on the wire. The response is always the complete effective document, so a caller can
/// never infer which codes carry persisted rows.</para>
///
/// <para>Authorization uses the plain capability boundary rather than the Product record-access
/// evaluator, exactly as the read does: this is a SYSTEM_CONFIGURATION resource with no record scope
/// and no field security, so applying Product record policies to it would let a rule about Product
/// records decide configuration mutation.</para>
/// </summary>
internal sealed class Handler(
    IAccessAuthorizer authorizer,
    IProductsPersistence persistence,
    TimeProvider timeProvider)
{
    private const string AggregateType = "PRODUCT_CONFIGURATION_TYPE";
    private const string Operation = "updateProductConfigurationType";

    internal async Task<ProductOperationResult<ProductConfigurationMutationResponse>> HandleAsync(
        Command command,
        CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeAsync(
            ProductConfigurationCapabilities.Configure,
            command.Metadata.CorrelationId,
            cancellationToken);
        if (!decision.IsAllowed || decision.Context is not { } context)
        {
            return Fail(string.Equals(decision.Code, "WORKSPACE_MISMATCH", StringComparison.Ordinal)
                ? ProductErrors.WorkspaceMismatch()
                : ProductErrors.AccessDenied());
        }

        var trusted = new TrustedWorkspaceContext(
            context.WorkspaceId,
            context.AccountId,
            context.MemberId,
            context.MembershipId);

        // Resource identity before body: a typeId outside the nine canonical codes, including a case
        // variant of one, identifies no resource at all. The vocabulary is contract-global and
        // identical in every Workspace, so this answer discloses no Workspace state - and a canonical
        // code that simply carries no override row is never reported as missing.
        if (!ProductConfigurationCatalog.IsCanonicalTypeCode(command.TypeId))
            return Fail(ProductErrors.NotFound());

        // An absent status and a status outside the enum are the same domain failure: neither names an
        // admitted effective state. The comparison is ordinal, so "active" is not "ACTIVE".
        var requestedStatus = command.Request.Status switch
        {
            ProductConfigurationCatalog.Active => ProductConfigurationCatalog.Active,
            ProductConfigurationCatalog.Inactive => ProductConfigurationCatalog.Inactive,
            _ => null
        };
        if (requestedStatus is null)
        {
            return Fail(ProductErrors.FieldValidation(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["status"] = ["status must be ACTIVE or INACTIVE."]
            }));
        }

        // The caller's command identity, and deliberately nothing else. Neither the configuration
        // revision nor the current status enters it: the same client request must not become a
        // different command merely because the Workspace configuration changed, and a committed
        // mutation stays replayable after the configuration later moves on.
        var fingerprint = ProductCommandSupport.Fingerprint(new { command.TypeId, Status = requestedStatus });
        var scopeKey = ProductCommandSupport.ScopeKey(trusted, Operation, command.TypeId, command.Metadata.IdempotencyKey);

        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);
        var existing = await persistence.FindIdempotencyAsync(scopeKey, cancellationToken);
        if (existing is not null)
        {
            // Answered from stored evidence alone. The replay writes nothing, so it establishes no
            // new trust and cannot advance the revision.
            var replayError = ProductCommandSupport.ReplayError(existing, fingerprint);
            return replayError is null
                ? ProductOperationResult<ProductConfigurationMutationResponse>.Success(
                    ProductCommandSupport.ReplayConfiguration(existing))
                : Fail(replayError);
        }

        // The snapshot the whole decision is made from, update-locked for the rest of this
        // transaction. There is no separate pre-check outside it, and the public read handler is
        // never called: the command would then acquire that operation's studio.read requirement and
        // leave a window in which a competing change could commit between check and write.
        var state = await persistence.LockProductConfigurationForMutationAsync(trusted.WorkspaceId, cancellationToken);
        var projected = ProductConfigurationCatalog.Project(state);
        if (!projected.IsSuccess)
        {
            // Corrupt owner-owned state fails the mutation closed. A mutation is an ordinary command,
            // not a recovery operation: nothing is normalised, nothing is partially written and
            // nothing is repaired, and the transaction is abandoned without a commit.
            return Fail(projected.Error!);
        }

        var document = projected.Value!;
        var expectedVersion = command.Metadata.ExpectedVersion!.Value;
        if (document.Revision != expectedVersion)
        {
            // If-Match guards the Workspace Product Configuration document revision, never an
            // individual override row.
            return Fail(ProductErrors.VersionConflict(command.TypeId, expectedVersion, document.Revision));
        }

        var currentStatus = document.Data.Types
            .Single(entry => string.Equals(entry.Code, command.TypeId, StringComparison.Ordinal))
            .Status;
        var changed = !string.Equals(currentStatus, requestedStatus, StringComparison.Ordinal);
        var revision = changed ? document.Revision + 1 : document.Revision;

        if (changed)
        {
            // Model B's persistence consequence: INACTIVE persists or retains the deviation row, and
            // ACTIVE removes it so the canonical default governs again.
            await persistence.ApplyProductConfigurationTypeStatusAsync(
                trusted.WorkspaceId,
                command.TypeId,
                string.Equals(requestedStatus, ProductConfigurationCatalog.Inactive, StringComparison.Ordinal)
                    ? ProductConfigurationCatalog.Inactive
                    : null,
                revision,
                cancellationToken);
        }

        // The response carries this revision as a strong ETag, so it becomes a served revision and a
        // later rollback below it must stay detectable. The raise is monotonic and shares this
        // transaction, so a failed command establishes no trust. On a semantic no-op it re-attests
        // the unchanged revision and advances nothing.
        await persistence.RaiseProductConfigurationTrustAsync(trusted.WorkspaceId, revision, cancellationToken);

        var now = timeProvider.GetUtcNow();
        var result = new ConfigurationDocumentResponse(
            revision,
            changed ? Retarget(document.Data, command.TypeId, requestedStatus) : document.Data);
        var audit = new ProductAuditRecord(
            Operation,
            trusted.WorkspaceId,
            trusted.MemberId,
            command.TypeId,
            command.Metadata.RequestId,
            command.Metadata.CorrelationId,
            "COMMITTED",
            document.Revision,
            revision,
            now);
        var response = new ProductConfigurationMutationResponse(
            ProductIds.New("command"),
            command.Metadata.CorrelationId,
            command.TypeId,
            AggregateType,
            revision,
            ProductProjection.Utc(now),
            // A same-status update is a success that was accepted and applied; it simply changed
            // nothing. It is deliberately COMMITTED and not REPLAYED, which is reserved for an
            // idempotency replay.
            "COMMITTED",
            result,
            [],
            // No Product Configuration event contract exists, so the outbox expectation is DEFERRED
            // and no event type is invented here.
            [],
            [audit.AuditId]);
        persistence.AddAudit(audit);
        persistence.AddIdempotency(new ProductIdempotencyRecord(
            scopeKey,
            trusted.WorkspaceId,
            Operation,
            trusted.MemberId,
            command.TypeId,
            command.Metadata.IdempotencyKey,
            fingerprint,
            JsonSerializer.Serialize(response, ProductCommandSupport.SerializationOptions),
            now));

        // Configuration mutation, revision advance, idempotency completion and audit are saved and
        // committed as one unit. There is no state in which the revision moved but the evidence did
        // not, or in which the command is replayable without having been applied.
        try
        {
            await persistence.SaveChangesAsync(cancellationToken);
        }
        catch (ProductsPersistenceConcurrencyException)
        {
            return Fail(ProductErrors.VersionConflict(command.TypeId, expectedVersion, document.Revision));
        }
        await transaction.CommitAsync(cancellationToken);
        return ProductOperationResult<ProductConfigurationMutationResponse>.Success(response);
    }

    /// <summary>
    /// Projects the post-command document by replacing the mutated entry in place. Canonical order is
    /// preserved because the entry keeps its position, and every one of the nine codes stays present.
    /// </summary>
    private static ProductConfigurationData Retarget(ProductConfigurationData data, string typeId, string status)
    {
        var entries = new ProductConfigurationTypeEntry[data.Types.Count];
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = data.Types[index];
            entries[index] = string.Equals(entry.Code, typeId, StringComparison.Ordinal)
                ? entry with { Status = status }
                : entry;
        }
        return new ProductConfigurationData(entries);
    }

    private static ProductOperationResult<ProductConfigurationMutationResponse> Fail(ProductOperationError error) =>
        ProductOperationResult<ProductConfigurationMutationResponse>.Failure(error);
}
