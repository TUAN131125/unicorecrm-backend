using UnicoreCRM.AI.Context;
using UnicoreCRM.AI.Prompts;
using UnicoreCRM.AI.Providers;
using UnicoreCRM.AI.Usage;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.AI.Gateway;

internal sealed class AiAdvisoryApplication(
    ICurrentWorkspace currentWorkspace,
    AiContextComposer contextComposer,
    AiPromptComposer promptComposer,
    IAiProvider provider,
    AiProviderOutputValidator outputValidator,
    AiProviderRuntimeOptions providerOptions,
    IAiUsageRecorder usageRecorder,
    TimeProvider timeProvider)
{
    internal async Task<AiOperationResult<AiAdvisoryResponse>> HandleAsync(
        AiAdvisoryRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var validation = Validate(request);
        if (validation.Error is not null)
            return AiOperationResult<AiAdvisoryResponse>.Failure(validation.Error);
        if (!currentWorkspace.IsResolved)
            return AiOperationResult<AiAdvisoryResponse>.Failure(AiErrors.WorkspaceMismatch());

        var executionId = $"ai_exec_{Guid.NewGuid():N}";
        var started = timeProvider.GetTimestamp();
        var startedAt = timeProvider.GetUtcNow();
        var trusted = currentWorkspace.Require();
        var contextResult = await contextComposer.LoadAsync(
            validation.ContextReferences!, executionId, correlationId, cancellationToken);
        if (!contextResult.IsSuccess)
        {
            await RecordUsageAsync(executionId, trusted, contextResult.ToolNames, contextResult.Items, contextResult.Error!.Code, started, startedAt);
            return AiOperationResult<AiAdvisoryResponse>.Failure(contextResult.Error);
        }

        var prompt = promptComposer.Compose(validation.Question!, validation.Locale!, contextResult.Items, validation.Conversation!);
        var providerRequest = new AiProviderRequest(
            executionId,
            prompt.SystemInstruction,
            prompt.UserInstruction,
            prompt.ContextData,
            validation.Locale!,
            contextResult.Items.Count);

        AiProviderResponse providerResponse;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(providerOptions.Timeout);
        try
        {
            providerResponse = await provider.CompleteAsync(providerRequest, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await RecordUsageAsync(executionId, trusted, contextResult.ToolNames, contextResult.Items, "AI_PROVIDER_TIMEOUT", started, startedAt);
            return AiOperationResult<AiAdvisoryResponse>.Failure(AiErrors.ProviderTimeout());
        }
        catch (AiProviderUnavailableException)
        {
            await RecordUsageAsync(executionId, trusted, contextResult.ToolNames, contextResult.Items, "AI_PROVIDER_UNAVAILABLE", started, startedAt);
            return AiOperationResult<AiAdvisoryResponse>.Failure(AiErrors.ProviderUnavailable());
        }
        catch (AiProviderRateLimitedException)
        {
            await RecordUsageAsync(executionId, trusted, contextResult.ToolNames, contextResult.Items, "AI_PROVIDER_RATE_LIMITED", started, startedAt);
            return AiOperationResult<AiAdvisoryResponse>.Failure(AiErrors.ProviderRateLimited());
        }
        catch (AiProviderInvalidResponseException)
        {
            await RecordUsageAsync(executionId, trusted, contextResult.ToolNames, contextResult.Items, "AI_PROVIDER_RESPONSE_INVALID", started, startedAt);
            return AiOperationResult<AiAdvisoryResponse>.Failure(AiErrors.InvalidProviderResponse());
        }
        catch (AiProviderSafetyRefusalException)
        {
            await RecordUsageAsync(executionId, trusted, contextResult.ToolNames, contextResult.Items, "AI_PROVIDER_SAFETY_REFUSAL", started, startedAt);
            return AiOperationResult<AiAdvisoryResponse>.Failure(AiErrors.ProviderSafetyRefusal());
        }

        var advisory = outputValidator.Validate(providerResponse.Content);
        if (advisory is null)
        {
            await RecordUsageAsync(executionId, trusted, contextResult.ToolNames, contextResult.Items, "AI_PROVIDER_RESPONSE_INVALID", started, startedAt, providerResponse);
            return AiOperationResult<AiAdvisoryResponse>.Failure(AiErrors.InvalidProviderResponse());
        }

        var response = new AiAdvisoryResponse(
            executionId,
            advisory.Summary,
            advisory.SuggestedNextAction,
            advisory.AttentionPoints,
            true,
            validation.ContextReferences!,
            contextResult.Items.Select(item => new AiGroundingEvidence(item.EntityType, item.EntityId, item.DisplayLabel, item.Version, item.ContextType!)).ToArray(),
            new AiAdvisoryProviderView(providerResponse.Provider ?? provider.Descriptor.Name, providerResponse.Model ?? provider.Descriptor.Model));
        await RecordUsageAsync(executionId, trusted, contextResult.ToolNames, contextResult.Items, "SUCCEEDED", started, startedAt, providerResponse);
        return AiOperationResult<AiAdvisoryResponse>.Success(response);
    }

    private static (string? Question, string? Locale, IReadOnlyList<AiContextReference>? ContextReferences, IReadOnlyList<AiConversationMessage>? Conversation, AiOperationError? Error)
        Validate(AiAdvisoryRequest request)
    {
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var question = request.Question?.Trim();
        if (string.IsNullOrEmpty(question) || question.Length > 2000)
            fields["question"] = ["question must contain between 1 and 2000 characters."];

        var locale = string.IsNullOrWhiteSpace(request.Locale) ? "en" : request.Locale.Trim().ToLowerInvariant();
        if (locale is not ("en" or "vi"))
            fields["locale"] = ["locale must be en or vi."];

        var supported = new HashSet<string>(["lead", "contact", "organization", "customer", "deal", "task"], StringComparer.OrdinalIgnoreCase);
        var references = request.ContextReferences;
        if (references is null || references.Count is 0 || references.Count > 6)
            fields["contextReferences"] = ["Between one and six context references are required."];
        else if (references.Any(x => string.IsNullOrWhiteSpace(x.Type) || string.IsNullOrWhiteSpace(x.Id)))
            fields["contextReferences"] = ["Every context reference requires a type and id."];
        else if (references.Any(x => !supported.Contains(x.Type!)))
            fields["contextReferences"] = ["A context reference type is unsupported."];

        var conversation = request.Conversation ?? [];
        if (conversation.Count > 12 || conversation.Any(x => x.Role is not ("user" or "assistant") || string.IsNullOrWhiteSpace(x.Content) || x.Content.Length > 2000) || conversation.Sum(x => x.Content!.Length) > 8000)
            fields["conversation"] = ["Conversation must contain at most 12 user/assistant messages, 2000 characters each, and 8000 characters total."];

        return fields.Count == 0
            ? (question, locale, references!.Select(x => new AiContextReference(x.Type!.Trim().ToLowerInvariant(), x.Id!.Trim())).ToArray(), conversation.Select(x => new AiConversationMessage(x.Role, x.Content!.Trim())).ToArray(), null)
            : (null, null, null, null, AiErrors.Invalid(fields));
    }

    private Task RecordUsageAsync(
        string executionId,
        TrustedWorkspaceContext trusted,
        IReadOnlyList<string> toolNames,
        IReadOnlyList<AiContextItem> contextItems,
        string status,
        long started,
        DateTimeOffset startedAt,
        AiProviderResponse? providerResponse = null)
    {
        return usageRecorder.RecordAsync(new AiUsageEvent(
            executionId,
            trusted.WorkspaceId,
            trusted.MemberId,
            providerResponse?.Provider ?? provider.Descriptor.Name,
            providerResponse?.Model ?? provider.Descriptor.Model,
            "requestAiAdvisory",
            toolNames,
            contextItems.SelectMany(item => item.Fields.Keys.Select(field => $"{item.EntityType}:{field}")).ToArray(),
            status,
            timeProvider.GetElapsedTime(started),
            startedAt,
            contextItems.Select(item => $"{item.EntityType}:{item.EntityId}").ToArray(),
            providerResponse?.InputTokens,
            providerResponse?.OutputTokens,
            providerResponse?.RequestId), CancellationToken.None);
    }
}
