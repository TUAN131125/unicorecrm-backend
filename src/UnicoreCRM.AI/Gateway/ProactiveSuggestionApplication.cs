using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using UnicoreCRM.AI.Prompts;
using UnicoreCRM.AI.Providers;
using UnicoreCRM.AI.Usage;
using UnicoreCRM.Crm.Customers.Contracts;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.PlatformOperations.AiExecution.Contracts;

namespace UnicoreCRM.AI.Gateway;

internal sealed class ProactiveSuggestionApplication(
    ICurrentWorkspace currentWorkspace, IAccessAuthorizer authorizer, IProactiveStore store,
    IProactiveCustomerAiContextReader customers, IAiProvider provider, IAiUsageRecorder usageRecorder,
    IServiceScopeFactory scopeFactory, TimeProvider clock)
{
    internal async Task<AiOperationResult<ProactiveSuggestionResponse>> HandleAsync(string itemId,
        ProactiveSuggestionRequest request, string requestId, string correlationId, CancellationToken cancellationToken)
    {
        var locale = string.IsNullOrWhiteSpace(request.Locale) ? "en" : request.Locale.Trim().ToLowerInvariant();
        if (locale is not ("vi" or "en"))
            return Failure(AiErrors.Invalid(new Dictionary<string, string[]> { ["locale"] = ["locale must be en or vi."] }));
        if (!currentWorkspace.IsResolved) return Failure(AiErrors.WorkspaceMismatch());
        var access = await authorizer.AuthorizeAsync(AccessRequirement.ForCanonicalCapability("ai.proactive.use"), correlationId, cancellationToken);
        if (!access.IsAllowed) return Failure(AiErrors.ProactiveAccessDenied());
        var trusted = currentWorkspace.Require();
        var item = await store.ReadItemAsync(trusted.WorkspaceId, itemId, cancellationToken);
        if (item is null || item.WorkspaceId != trusted.WorkspaceId || item.SubjectType != ProactiveValues.Customer
            || item.TriggerType != ProactiveValues.CustomerHealthRisk || item.Status != ProactiveValues.Open
            || item.OwnerMemberId != trusted.MemberId) return Failure(AiErrors.ProactiveNotFound());
        var customerResult = await customers.ReadAsync(item.SubjectId, new(requestId, correlationId), cancellationToken);
        if (!customerResult.IsAuthorized) return Failure(AiErrors.ProactiveAccessDenied());
        if (customerResult.Context is not { } customer) return Failure(AiErrors.ProactiveNotFound());
        var why = ProactiveWhyFormatter.Format(item, customer.HealthBand, locale);
        var executionId = $"ai_exec_{Guid.NewGuid():N}";
        var started = clock.GetTimestamp();
        var startedAt = clock.GetUtcNow();

        // A separate scope guarantees that audits never flush failed ledger or business changes.
        await AuditAsync("AI_SUGGESTION_REQUESTED", null, cancellationToken);
        AiProviderResponse? providerResponse = null;
        ProactiveSuggestionOutput? output = null;
        AiOperationError? error = null;
        var outcome = "AI_PROVIDER_UNAVAILABLE";
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var prompt = ProactiveSuggestionPromptComposer.Compose(executionId, locale, item, customer);
            try
            {
                providerResponse = await provider.CompleteAsync(prompt, cancellationToken);
                output = ProactiveSuggestionOutputValidator.Validate(providerResponse.Content);
                if (output is null) error = AiErrors.InvalidProviderResponse();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { error = AiErrors.ProviderTimeout(); }
            catch (AiProviderUnavailableException) { error = AiErrors.ProviderUnavailable(); }
            catch (AiProviderRateLimitedException) { error = AiErrors.ProviderRateLimited(); }
            catch (AiProviderInvalidResponseException) { error = AiErrors.InvalidProviderResponse(); }
            catch (AiProviderSafetyRefusalException) { error = AiErrors.ProviderSafetyRefusal(); }
            outcome = error?.Code ?? "SUCCEEDED";
        }
        catch (OperationCanceledException)
        {
            outcome = "AI_REQUEST_CANCELLED";
            throw;
        }
        finally
        {
            try
            {
                await usageRecorder.RecordAsync(new(executionId, trusted.WorkspaceId, trusted.MemberId,
                    providerResponse?.Provider ?? provider.Descriptor.Name, providerResponse?.Model ?? provider.Descriptor.Model,
                    "requestProactiveSuggestion", [], [], outcome, clock.GetElapsedTime(started), startedAt,
                    [$"customer:{customer.CustomerId}", $"proactive:{item.ItemId}"], providerResponse?.InputTokens,
                    providerResponse?.OutputTokens, providerResponse?.RequestId), CancellationToken.None);
            }
            catch
            {
                outcome = "AI_EXECUTION_RECORD_FAILED";
                throw;
            }
            finally
            {
                await AuditAsync(outcome == "SUCCEEDED" ? "AI_SUGGESTION_SUCCEEDED" : "AI_SUGGESTION_FAILED",
                    outcome, CancellationToken.None);
            }
        }
        return error is not null ? Failure(error) : AiOperationResult<ProactiveSuggestionResponse>.Success(
            new(executionId, item.ItemId, customer.CustomerId, output!.Summary, why, output.SuggestedNextStep, output.TaskDraft));

        async Task AuditAsync(string action, string? result, CancellationToken token)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IProactiveStore>().RecordAuditAsync(
                new($"proaudit_{Guid.NewGuid():N}", trusted.WorkspaceId, trusted.MemberId, action, item.ItemId, customer.CustomerId,
                    JsonSerializer.Serialize(new { executionId, outcome = result }), correlationId, clock.GetUtcNow()), token);
        }
    }

    private static AiOperationResult<ProactiveSuggestionResponse> Failure(AiOperationError error) =>
        AiOperationResult<ProactiveSuggestionResponse>.Failure(error);
}
