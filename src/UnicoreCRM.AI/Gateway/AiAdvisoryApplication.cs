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
        var trusted = currentWorkspace.Require();
        var contextResult = await contextComposer.LoadAsync(
            validation.ContextReferences!, executionId, correlationId, cancellationToken);
        if (!contextResult.IsSuccess)
        {
            RecordUsage(executionId, trusted, contextResult.ToolNames, contextResult.Items, contextResult.Error!.Code, started);
            return AiOperationResult<AiAdvisoryResponse>.Failure(contextResult.Error);
        }

        var prompt = promptComposer.Compose(
            validation.Question!, validation.Locale!, contextResult.Items);
        var providerRequest = new AiProviderRequest(
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
            RecordUsage(executionId, trusted, contextResult.ToolNames, contextResult.Items, "AI_PROVIDER_TIMEOUT", started);
            return AiOperationResult<AiAdvisoryResponse>.Failure(AiErrors.ProviderTimeout());
        }
        catch (AiProviderUnavailableException)
        {
            RecordUsage(executionId, trusted, contextResult.ToolNames, contextResult.Items, "AI_PROVIDER_UNAVAILABLE", started);
            return AiOperationResult<AiAdvisoryResponse>.Failure(AiErrors.ProviderUnavailable());
        }

        var advisory = outputValidator.Validate(providerResponse.Content);
        if (advisory is null)
        {
            RecordUsage(executionId, trusted, contextResult.ToolNames, contextResult.Items, "AI_PROVIDER_RESPONSE_INVALID", started);
            return AiOperationResult<AiAdvisoryResponse>.Failure(AiErrors.InvalidProviderResponse());
        }

        var response = new AiAdvisoryResponse(
            executionId,
            advisory.Summary,
            advisory.SuggestedNextAction,
            advisory.AttentionPoints,
            true,
            validation.ContextReferences!,
            new AiAdvisoryProviderView(provider.Descriptor.Name, provider.Descriptor.Model));
        RecordUsage(executionId, trusted, contextResult.ToolNames, contextResult.Items, "SUCCEEDED", started);
        return AiOperationResult<AiAdvisoryResponse>.Success(response);
    }

    private static (string? Question, string? Locale, AiAdvisoryContextReferences? ContextReferences, AiOperationError? Error)
        Validate(AiAdvisoryRequest request)
    {
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var question = request.Question?.Trim();
        if (string.IsNullOrEmpty(question) || question.Length > 2000)
            fields["question"] = ["question must contain between 1 and 2000 characters."];

        var locale = string.IsNullOrWhiteSpace(request.Locale) ? "en" : request.Locale.Trim().ToLowerInvariant();
        if (locale is not ("en" or "vi"))
            fields["locale"] = ["locale must be en or vi."];

        var references = request.ContextReferences;
        var referenceCount = references is null
            ? 0
            : new[] { references.LeadId, references.DealId, references.TaskId }
                .Count(value => !string.IsNullOrWhiteSpace(value));
        if (referenceCount == 0)
            fields["contextReferences"] = ["At least one Lead, Deal, or Task reference is required."];

        return fields.Count == 0
            ? (question, locale, new AiAdvisoryContextReferences(
                references!.LeadId?.Trim(),
                references.DealId?.Trim(),
                references.TaskId?.Trim()), null)
            : (null, null, null, AiErrors.Invalid(fields));
    }

    private void RecordUsage(
        string executionId,
        TrustedWorkspaceContext trusted,
        IReadOnlyList<string> toolNames,
        IReadOnlyList<AiContextItem> contextItems,
        string status,
        long started)
    {
        usageRecorder.Record(new AiUsageEvent(
            executionId,
            trusted.WorkspaceId,
            trusted.MemberId,
            provider.Descriptor.Name,
            provider.Descriptor.Model,
            "requestAiAdvisory",
            toolNames,
            contextItems.SelectMany(item => item.Fields.Keys.Select(field => $"{item.EntityType}:{field}")).ToArray(),
            status,
            timeProvider.GetElapsedTime(started)));
    }
}
