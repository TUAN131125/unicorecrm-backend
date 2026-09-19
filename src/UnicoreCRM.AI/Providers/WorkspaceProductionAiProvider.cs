using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.PlatformOperations.AiExecution.Contracts;

namespace UnicoreCRM.AI.Providers;

internal sealed class WorkspaceProductionAiProvider(
    WorkspaceAiProviderResolver resolver,
    IEnumerable<IProductionAiProviderAdapter> adapters,
    ICurrentWorkspace currentWorkspace,
    IAiExecutionLedger ledger,
    AiProviderCircuitBreaker circuitBreaker,
    AiWorkspaceGuardrails guardrails,
    AiProviderRuntimeOptions options,
    TimeProvider timeProvider) : IAiProvider
{
    private AiProviderDescriptor descriptor = new("WORKSPACE_RESOLVED", "SERVER_SELECTED");
    public AiProviderDescriptor Descriptor => descriptor;

    public async Task<AiProviderResponse> CompleteAsync(AiProviderRequest request, CancellationToken cancellationToken)
    {
        using var guardrailLease = await guardrails.EnterAsync(currentWorkspace.Require().WorkspaceId, cancellationToken);
        var policy = await resolver.ResolveAsync(cancellationToken);
        var targets = new List<(ResolvedProviderTarget Target, string Kind)> { (policy.Primary, "PRIMARY"), (policy.Primary, "RETRY") };
        if (policy.Fallback is not null) targets.Add((policy.Fallback, "FALLBACK"));
        AiProviderExecutionException? last = null;
        var primaryCircuitOpen = false;
        var attemptNumber = 0;
        for (var index = 0; index < targets.Count; index++)
        {
            var (target, kind) = targets[index];
            if (kind == "RETRY" && primaryCircuitOpen) continue;
            if (kind == "RETRY" && last is not null && !Retryable(last.Failure, policy.RetryRateLimited)) continue;
            if (kind == "FALLBACK" && last is not null && !FailoverEligible(last.Failure, policy.RetryRateLimited)) break;
            var scope = $"{currentWorkspace.Require().WorkspaceId}:{target.Provider}:{target.Model}";
            attemptNumber++;
            var startedAt = timeProvider.GetUtcNow(); var started = timeProvider.GetTimestamp();
            if (!circuitBreaker.TryEnter(scope))
            {
                last = new(AiProviderFailure.Unavailable, "Provider circuit is open.");
                primaryCircuitOpen = kind == "PRIMARY";
                await RecordAttempt(request.ExecutionId, attemptNumber, kind, target, startedAt, started, "CIRCUIT_OPEN", null, last);
                continue;
            }
            var adapter = adapters.SingleOrDefault(x => x.ProviderId == target.Provider) ?? throw new AiProviderUnavailableException();
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(options.Timeout);
                var result = await adapter.CompleteAsync(target.Model, target.Credential, request, timeout.Token);
                circuitBreaker.Success(scope); descriptor = new(result.Provider, result.Model);
                await RecordAttempt(request.ExecutionId, attemptNumber, kind, target, startedAt, started, "SUCCEEDED", result, null);
                return new(result.Content, result.ProviderRequestId, result.InputTokens, result.OutputTokens, result.Provider, result.Model);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                last = new(AiProviderFailure.Timeout, "Provider request timed out."); circuitBreaker.Failure(scope);
                await RecordAttempt(request.ExecutionId, attemptNumber, kind, target, startedAt, started, "TIMEOUT", null, last);
            }
            catch (OperationCanceledException)
            {
                await RecordAttempt(request.ExecutionId, attemptNumber, kind, target, startedAt, started, "CANCELLED", null,
                    new(AiProviderFailure.Cancelled, "Provider request was cancelled.")); throw;
            }
            catch (AiProviderExecutionException exception)
            {
                last = exception; if (Retryable(exception.Failure, policy.RetryRateLimited)) circuitBreaker.Failure(scope);
                await RecordAttempt(request.ExecutionId, attemptNumber, kind, target, startedAt, started, "FAILED", null, exception);
            }
        }
        throw Map(last);
    }

    private async Task RecordAttempt(string executionId, int number, string kind, ResolvedProviderTarget target, DateTimeOffset startedAt,
        long started, string status, AiProviderAdapterResult? result, AiProviderExecutionException? error)
    {
        var completed = timeProvider.GetUtcNow();
        await ledger.RecordAttemptAsync(new($"ai_attempt_{Guid.NewGuid():N}", executionId, currentWorkspace.Require().WorkspaceId, number, kind,
            target.Provider, target.Model, startedAt, completed, (long)timeProvider.GetElapsedTime(started).TotalMilliseconds, status,
            result?.ProviderRequestId, result?.InputTokens, result?.OutputTokens, error?.Failure.ToString().ToUpperInvariant(), error?.SafeDiagnostic), CancellationToken.None);
    }

    private static bool Retryable(AiProviderFailure failure, bool rateLimited) => failure is AiProviderFailure.NetworkFailure or AiProviderFailure.Unavailable or AiProviderFailure.Timeout || rateLimited && failure == AiProviderFailure.RateLimited;
    private static bool FailoverEligible(AiProviderFailure failure, bool rateLimited) => Retryable(failure, rateLimited);
    private static Exception Map(AiProviderExecutionException? error) => error?.Failure switch
    {
        AiProviderFailure.RateLimited => new AiProviderRateLimitedException(),
        AiProviderFailure.Timeout => new OperationCanceledException("Provider request timed out."),
        AiProviderFailure.Cancelled => new OperationCanceledException("Provider request cancelled."),
        AiProviderFailure.InvalidResponse => new AiProviderInvalidResponseException(),
        AiProviderFailure.SafetyRefusal => new AiProviderSafetyRefusalException(),
        _ => new AiProviderUnavailableException()
    };
}
