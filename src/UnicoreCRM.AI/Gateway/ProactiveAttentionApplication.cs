using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UnicoreCRM.Crm.Customers.Contracts;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.PlatformOperations.AiExecution.Contracts;

namespace UnicoreCRM.AI.Gateway;

internal sealed class ProactiveAttentionApplication(
    ICurrentWorkspace currentWorkspace,
    IAccessAuthorizer authorizer,
    IProactiveStore store,
    ICustomerAttentionReader customers,
    TimeProvider clock)
{
    private static readonly AccessRequirement Use = AccessRequirement.ForCanonicalCapability("ai.proactive.use");
    private static readonly AccessRequirement Manage = AccessRequirement.ForCanonicalCapability("ai.proactive.manage");

    internal async Task<AiOperationResult<ProactiveItemPage>> ListAsync(string? cursor, int limit, string requestId, string correlationId, CancellationToken ct)
    {
        var access = await AuthorizedAsync(Use, correlationId, ct);
        if (access.Error is not null) return AiOperationResult<ProactiveItemPage>.Failure(access.Error);
        if (limit is < 1 or > 100)
            return AiOperationResult<ProactiveItemPage>.Failure(AiErrors.Invalid(new Dictionary<string, string[]> { ["limit"] = ["limit must be between 1 and 100."] }));
        if (!TryCursor(cursor, out var before, out var beforeId))
            return AiOperationResult<ProactiveItemPage>.Failure(AiErrors.Invalid(new Dictionary<string, string[]> { ["cursor"] = ["Pagination cursor is invalid."] }));
        var context = access.Context!;
        var rows = await store.ReadOwnerOpenItemsAsync(context.WorkspaceId, context.MemberId, before, beforeId, limit + 1, ct);
        var candidates = rows.Take(limit).ToArray();
        var authorized = await customers.ReadAuthorizedAsync(candidates.Select(x => x.SubjectId).ToArray(), new(requestId, correlationId), ct);
        if (!authorized.IsAuthorized) return AiOperationResult<ProactiveItemPage>.Failure(AiErrors.ProactiveAccessDenied());
        var visible = candidates.Where(item => authorized.Items.ContainsKey(item.SubjectId))
            .Select(item => Project(item, authorized.Items[item.SubjectId])).ToArray();
        var next = rows.Count > limit && candidates.Length != 0 ? Cursor(candidates[^1]) : null;
        return AiOperationResult<ProactiveItemPage>.Success(new(visible, next));
    }

    internal async Task<AiOperationResult<ProactiveItemView>> DetailAsync(string itemId, string requestId, string correlationId, CancellationToken ct)
    {
        var authorized = await AuthorizeItemAsync(itemId, requestId, correlationId, ct);
        if (authorized.Error is not null) return AiOperationResult<ProactiveItemView>.Failure(authorized.Error);
        if (authorized.Item!.Status != ProactiveValues.Open) return AiOperationResult<ProactiveItemView>.Failure(AiErrors.ProactiveNotFound());
        return AiOperationResult<ProactiveItemView>.Success(Project(authorized.Item, authorized.Customer!));
    }

    internal Task<AiOperationResult<ProactiveMutationResponse>> SeenAsync(string itemId, long expectedVersion, string key, string requestId, string correlationId, CancellationToken ct) =>
        MutateAsync(itemId, expectedVersion, key, "seenProactiveItem", null, requestId, correlationId, ct);

    internal Task<AiOperationResult<ProactiveMutationResponse>> DismissAsync(string itemId, long expectedVersion, string key, string requestId, string correlationId, CancellationToken ct) =>
        MutateAsync(itemId, expectedVersion, key, "dismissProactiveItem", null, requestId, correlationId, ct);

    internal Task<AiOperationResult<ProactiveMutationResponse>> SnoozeAsync(string itemId, DateTimeOffset until, long expectedVersion, string key, string requestId, string correlationId, CancellationToken ct) =>
        MutateAsync(itemId, expectedVersion, key, "snoozeProactiveItem", until, requestId, correlationId, ct);

    internal async Task<AiOperationResult<ProactiveConfigurationView>> GetConfigurationAsync(string correlationId, CancellationToken ct)
    {
        var access = await AuthorizedAsync(Manage, correlationId, ct);
        if (access.Error is not null) return AiOperationResult<ProactiveConfigurationView>.Failure(access.Error);
        var state = await store.ReadPolicyAsync(access.Context!.WorkspaceId, ct);
        return AiOperationResult<ProactiveConfigurationView>.Success(state is null
            ? new(false, 0, null, null, null)
            : Configuration(state));
    }

    internal async Task<AiOperationResult<ProactiveConfigurationView>> SaveConfigurationAsync(bool enabled, long expectedVersion,
        string key, string correlationId, CancellationToken ct)
    {
        var access = await AuthorizedAsync(Manage, correlationId, ct);
        if (access.Error is not null) return AiOperationResult<ProactiveConfigurationView>.Failure(access.Error);
        var context = access.Context!;
        var now = clock.GetUtcNow();
        var fingerprint = Fingerprint("CONFIGURE", new { enabled, expectedVersion });
        var audit = new ProactiveAuditEvidence($"proaudit_{Guid.NewGuid():N}", context.WorkspaceId, context.MemberId,
            enabled ? "PROACTIVE_ENABLED" : "PROACTIVE_DISABLED", null, null, "{}", correlationId, now);
        var commit = await store.SavePolicyConfigurationAsync(context.WorkspaceId, context.MemberId, enabled, expectedVersion,
            key, fingerprint, audit, now, ct);
        return commit.Status switch
        {
            ProactiveCommitStatus.Committed or ProactiveCommitStatus.Replayed => AiOperationResult<ProactiveConfigurationView>.Success(Configuration(commit.State!)),
            ProactiveCommitStatus.VersionConflict => AiOperationResult<ProactiveConfigurationView>.Failure(AiErrors.ProactiveVersionConflict()),
            _ => AiOperationResult<ProactiveConfigurationView>.Failure(AiErrors.ProactiveIdempotencyConflict())
        };
    }

    private async Task<AiOperationResult<ProactiveMutationResponse>> MutateAsync(string itemId, long expectedVersion, string key,
        string operation, DateTimeOffset? until, string requestId, string correlationId, CancellationToken ct)
    {
        var authorized = await AuthorizeItemAsync(itemId, requestId, correlationId, ct);
        if (authorized.Error is not null) return AiOperationResult<ProactiveMutationResponse>.Failure(authorized.Error);
        var current = authorized.Item!;
        var now = clock.GetUtcNow();
        if (operation == "snoozeProactiveItem" && (until is null || until <= now))
            return AiOperationResult<ProactiveMutationResponse>.Failure(AiErrors.Invalid(new Dictionary<string, string[]> { ["snoozedUntil"] = ["snoozedUntil must be a future instant."] }));
        if (current.Status == ProactiveValues.Resolved) return AiOperationResult<ProactiveMutationResponse>.Failure(AiErrors.ProactiveNotFound());
        var next = operation switch
        {
            "seenProactiveItem" => current with { SeenAt=now,Version=expectedVersion+1,UpdatedAt=now },
            "snoozeProactiveItem" => current with { Status=ProactiveValues.Snoozed,SnoozedUntil=until,Version=expectedVersion+1,UpdatedAt=now },
            _ => current with { Status=ProactiveValues.Dismissed,DismissedAt=now,SnoozedUntil=null,Version=expectedVersion+1,UpdatedAt=now }
        };
        var action = operation switch { "seenProactiveItem" => "ITEM_SEEN", "snoozeProactiveItem" => "ITEM_SNOOZED", _ => "ITEM_DISMISSED" };
        var fingerprint = Fingerprint(operation, new { itemId, expectedVersion, until });
        var audit = new ProactiveAuditEvidence($"proaudit_{Guid.NewGuid():N}", next.WorkspaceId, next.OwnerMemberId, action,
            next.ItemId, next.SubjectId, "{}", correlationId, now);
        var commit = await store.CommitItemActionAsync(next, next.OwnerMemberId, operation, expectedVersion, key, fingerprint, audit, ct);
        return commit.Status switch
        {
            ProactiveCommitStatus.Committed or ProactiveCommitStatus.Replayed => AiOperationResult<ProactiveMutationResponse>.Success(new(Project(commit.State!, authorized.Customer!))),
            ProactiveCommitStatus.VersionConflict => AiOperationResult<ProactiveMutationResponse>.Failure(AiErrors.ProactiveVersionConflict()),
            ProactiveCommitStatus.IdempotencyConflict => AiOperationResult<ProactiveMutationResponse>.Failure(AiErrors.ProactiveIdempotencyConflict()),
            _ => AiOperationResult<ProactiveMutationResponse>.Failure(AiErrors.ProactiveNotFound())
        };
    }

    private async Task<(ProactiveItemState? Item, CustomerAttentionProjection? Customer, AiOperationError? Error)> AuthorizeItemAsync(
        string itemId, string requestId, string correlationId, CancellationToken ct)
    {
        var access = await AuthorizedAsync(Use, correlationId, ct);
        if (access.Error is not null) return (null, null, access.Error);
        var context = access.Context!;
        var item = await store.ReadItemAsync(context.WorkspaceId, itemId, ct);
        if (item is null || item.SubjectType != ProactiveValues.Customer || item.OwnerMemberId != context.MemberId)
            return (null, null, AiErrors.ProactiveNotFound());
        var customerResult = await customers.ReadAuthorizedAsync([item.SubjectId], new(requestId, correlationId), ct);
        if (!customerResult.IsAuthorized) return (null, null, AiErrors.ProactiveAccessDenied());
        if (!customerResult.Items.TryGetValue(item.SubjectId, out var customer))
            return (null, null, AiErrors.ProactiveNotFound());
        return (item, customer, null);
    }

    private async Task<(TrustedWorkspaceContext? Context, AiOperationError? Error)> AuthorizedAsync(AccessRequirement requirement, string correlationId, CancellationToken ct)
    {
        if (!currentWorkspace.IsResolved) return (null, AiErrors.WorkspaceMismatch());
        var decision = await authorizer.AuthorizeAsync(requirement, correlationId, ct);
        return decision.IsAllowed ? (currentWorkspace.Require(), null) : (null, AiErrors.ProactiveAccessDenied());
    }

    private static ProactiveItemView Project(ProactiveItemState item, CustomerAttentionProjection customer) =>
        new(item.ItemId, item.SubjectId, customer.DisplayLabel, item.Severity, item.ReasonCode, item.Status,
            item.FirstDetectedAt, item.LastDetectedAt, item.SeenAt, item.SnoozedUntil, item.Version);
    private static ProactiveConfigurationView Configuration(WorkspaceProactivePolicyState state) =>
        new(state.Enabled, state.Version, state.LastEvaluationAt, state.NextEvaluationAt, state.UpdatedAt);
    private static string Fingerprint(string operation, object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(operation+":"+JsonSerializer.Serialize(value))));
    private static string Cursor(ProactiveItemState item) => Convert.ToBase64String(Encoding.UTF8.GetBytes($"{item.UpdatedAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}\n{item.ItemId}"))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static bool TryCursor(string? cursor, out DateTimeOffset? before, out string? itemId)
    {
        before=null;itemId=null;if(string.IsNullOrEmpty(cursor))return true;
        if (cursor.Length > 512) return false;
        try
        {
            var encoded=cursor.Replace('-', '+').Replace('_', '/');
            encoded += (encoded.Length % 4) switch { 2 => "==", 3 => "=", 0 => "", _ => throw new FormatException() };
            var value=Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            var parts=value.Split('\n');
            if(parts.Length!=2||!long.TryParse(parts[0],NumberStyles.None,CultureInfo.InvariantCulture,out var ticks)
                || ticks<DateTimeOffset.MinValue.UtcTicks||ticks>DateTimeOffset.MaxValue.UtcTicks
                || !Regex.IsMatch(parts[1], "^[A-Za-z0-9][A-Za-z0-9._:-]{0,79}$", RegexOptions.CultureInvariant))return false;
            before=new DateTimeOffset(ticks,TimeSpan.Zero);itemId=parts[1];return true;
        }
        catch(FormatException) { return false; }
        catch(ArgumentException) { return false; }
    }
}
