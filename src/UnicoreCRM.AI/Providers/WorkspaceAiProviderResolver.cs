using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.PlatformOperations.AiExecution.Contracts;

namespace UnicoreCRM.AI.Providers;

internal sealed record ResolvedProviderTarget(string Provider, string Model, string Credential);
internal sealed record ResolvedAiExecutionPolicy(ResolvedProviderTarget Primary, bool RetryRateLimited, ResolvedProviderTarget? Fallback);

internal sealed class WorkspaceAiProviderResolver(
    ICurrentWorkspace currentWorkspace,
    IWorkspaceAiConfigurationStore store,
    IDataProtectionProvider protectionProvider,
    IConfiguration configuration,
    AiProviderCatalog catalog)
{
    private readonly IDataProtector protector = protectionProvider.CreateProtector("UnicoreCRM.WorkspaceAiCredential.v1");

    internal async Task<ResolvedAiExecutionPolicy> ResolveAsync(CancellationToken cancellationToken)
    {
        var workspaceId = currentWorkspace.Require().WorkspaceId;
        var active = await store.FindActivePolicyAsync(workspaceId, cancellationToken);
        if (active is not null)
        {
            var primary = ResolveTarget(active.Policy.PrimaryProvider, active.Policy.PrimaryModel, active.Policy.PrimaryCredentialSource, active.PrimaryProtectedCredential);
            var fallback = active.Policy.FallbackEnabled
                ? ResolveTarget(active.Policy.FallbackProvider!, active.Policy.FallbackModel!, active.Policy.FallbackCredentialSource!, active.FallbackProtectedCredential)
                : null;
            return new(primary, active.Policy.RetryRateLimited, fallback);
        }

        var provider = (configuration["AI:DeploymentDefault:Provider"] ?? "GEMINI").Trim().ToUpperInvariant();
        var model = configuration["AI:DeploymentDefault:Model"] ?? (provider == "GEMINI" ? "gemini-2.5-flash" : "gpt-5-mini");
        return new(ResolveTarget(provider, model, AiConfigurationValues.DeploymentCredential, null), false, null);
    }

    private ResolvedProviderTarget ResolveTarget(string provider, string model, string source, string? protectedCredential)
    {
        if (!catalog.IsAdmitted(provider, model)) throw new AiProviderUnavailableException();
        string? credential;
        if (source == AiConfigurationValues.WorkspaceCredential)
        {
            try { credential = protectedCredential is null ? null : protector.Unprotect(protectedCredential); }
            catch (Exception) { throw new AiProviderUnavailableException(); }
        }
        else credential = configuration[$"AI:Providers:{provider}:ApiKey"];
        if (string.IsNullOrWhiteSpace(credential)) throw new AiProviderUnavailableException();
        return new(provider, model, credential);
    }
}

internal sealed class AiProviderCircuitBreaker(TimeProvider timeProvider, int threshold = 3, TimeSpan? cooldown = null)
{
    private sealed record State(int Failures, DateTimeOffset? OpenUntil, bool HalfOpenProbe);
    private readonly object gate = new(); private readonly Dictionary<string, State> states = new(StringComparer.Ordinal);
    private readonly TimeSpan openDuration = cooldown ?? TimeSpan.FromSeconds(30);

    internal AiProviderCircuitAdmission Admit(string scope)
    {
        lock (gate)
        {
            if (!states.TryGetValue(scope, out var state)) return new(this, scope, true, false);
            if (state.HalfOpenProbe) return new(this, scope, false, false);
            if (state.OpenUntil is null) return new(this, scope, true, false);
            if (state.OpenUntil <= timeProvider.GetUtcNow())
            {
                states[scope] = new(state.Failures, null, true);
                return new(this, scope, true, true);
            }
            return new(this, scope, false, false);
        }
    }

    private void Success(string scope) { lock (gate) states.Remove(scope); }
    private void TransientFailure(string scope)
    {
        lock (gate)
        {
            states.TryGetValue(scope, out var current); var failures = (current?.Failures ?? 0) + 1;
            states[scope] = new(failures, failures >= threshold || current?.HalfOpenProbe == true ? timeProvider.GetUtcNow().Add(openDuration) : null, false);
        }
    }

    private void Release(string scope, bool halfOpen)
    {
        if (!halfOpen) return;
        lock (gate)
        {
            if (states.TryGetValue(scope, out var current) && current.HalfOpenProbe)
                states[scope] = new(current.Failures, timeProvider.GetUtcNow().Add(openDuration), false);
        }
    }

    internal sealed class AiProviderCircuitAdmission(
        AiProviderCircuitBreaker owner,
        string scope,
        bool isAdmitted,
        bool halfOpen) : IDisposable
    {
        private int completed;
        internal bool IsAdmitted { get; } = isAdmitted;
        internal void Success() => Complete(() => owner.Success(scope));
        internal void TransientFailure() => Complete(() => owner.TransientFailure(scope));
        internal void Release() => Complete(() => owner.Release(scope, halfOpen));
        public void Dispose() => Release();
        private void Complete(Action transition)
        {
            if (IsAdmitted && Interlocked.Exchange(ref completed, 1) == 0) transition();
        }
    }
}
