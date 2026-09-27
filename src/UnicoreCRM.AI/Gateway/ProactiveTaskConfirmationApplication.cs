using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UnicoreCRM.Crm.Customers.Contracts;
using UnicoreCRM.Operations.Tasks.Contracts;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.PlatformOperations.AiExecution.Contracts;

namespace UnicoreCRM.AI.Gateway;

internal sealed class ProactiveTaskConfirmationApplication(ICurrentWorkspace currentWorkspace, IAccessAuthorizer authorizer,
    IProactiveStore store, ICustomerAttentionReader customers, IProactiveTaskCreationParticipant tasks,
    IServiceScopeFactory scopeFactory, TimeProvider clock, ILogger<ProactiveTaskConfirmationApplication> logger)
{
    internal async Task<AiOperationResult<ProactiveTaskConfirmationResponse>> HandleAsync(string itemId,
        ProactiveTaskConfirmationRequest request, string idempotencyKey, string requestId, string correlationId, CancellationToken ct)
    {
        if (idempotencyKey.Length is < 8 or > 128)
            return Failure(new("VALIDATION_FAILED", 422, "Request validation failed", FieldErrors:
                new Dictionary<string, string[]> { ["Idempotency-Key"] = ["Idempotency-Key must contain between 8 and 128 characters."] }));
        if (!currentWorkspace.IsResolved) return Failure(AiErrors.WorkspaceMismatch());
        var decision = await authorizer.AuthorizeAsync(AccessRequirement.ForCanonicalCapability("ai.proactive.use"), correlationId, ct);
        if (!decision.IsAllowed) return Failure(AiErrors.ProactiveAccessDenied());
        var trusted = currentWorkspace.Require();
        var item = await store.ReadItemAsync(trusted.WorkspaceId, itemId, ct);
        if (item is null || item.WorkspaceId != trusted.WorkspaceId || item.SubjectType != ProactiveValues.Customer
            || item.TriggerType != ProactiveValues.CustomerHealthRisk || item.Status != ProactiveValues.Open
            || item.OwnerMemberId != trusted.MemberId) return Failure(AiErrors.ProactiveNotFound());
        var customer = await customers.ReadAuthorizedAsync([item.SubjectId], new(requestId, correlationId), ct);
        if (!customer.IsAuthorized) return Failure(AiErrors.ProactiveAccessDenied());
        if (!customer.Items.ContainsKey(item.SubjectId)) return Failure(AiErrors.ProactiveNotFound());
        ct.ThrowIfCancellationRequested();
        var result = await tasks.CreateAsync(new(item.ItemId, request.Title, request.Description, request.AssigneeId,
            request.DueAt, request.Priority, idempotencyKey, requestId, correlationId), ct);
        if (result.Error is { } error) return Failure(new(error.Code, error.Status, error.Title, FieldErrors: error.FieldErrors));

        // Tasks owns the commit. This independent evidence write cannot roll it back.
        var auditId = "proaudit_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { trusted.WorkspaceId, trusted.MemberId, item.ItemId, result.TaskId }))));
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IProactiveStore>().RecordAuditOnceAsync(new(auditId,
                trusted.WorkspaceId, trusted.MemberId, "TASK_SUGGESTION_ACCEPTED", item.ItemId, item.SubjectId,
                JsonSerializer.Serialize(new { taskId = result.TaskId, outcome = result.Outcome }), correlationId, clock.GetUtcNow()), CancellationToken.None);
        }
        catch (Exception)
        {
            logger.LogWarning("Proactive acceptance audit unavailable for Workspace {WorkspaceId}, item {ItemId}, Task {TaskId}, correlation {CorrelationId}",
                trusted.WorkspaceId, item.ItemId, result.TaskId, correlationId);
            return Failure(new("AI_PROACTIVE_ACCEPTANCE_AUDIT_UNAVAILABLE", 503,
                "Task is committed; retry with the same Idempotency-Key to record acceptance.", true));
        }
        return AiOperationResult<ProactiveTaskConfirmationResponse>.Success(new(item.ItemId, result.TaskId!, result.TaskVersion!.Value, result.Outcome!));
    }

    private static AiOperationResult<ProactiveTaskConfirmationResponse> Failure(AiOperationError error) =>
        AiOperationResult<ProactiveTaskConfirmationResponse>.Failure(error);
}
