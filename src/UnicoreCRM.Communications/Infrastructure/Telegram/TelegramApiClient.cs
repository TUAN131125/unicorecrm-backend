using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace UnicoreCRM.Communications.Infrastructure.Telegram;

// Per-account resolution is independent of custody health and provider transport admission.
internal interface ITelegramCredentials
{
    ValueTask<TelegramCredential?> ResolveAsync(string workspace, Guid account, CancellationToken ct);
}
internal sealed class MissingTelegramCredentials : ITelegramCredentials
{
    public ValueTask<TelegramCredential?> ResolveAsync(string workspace, Guid account, CancellationToken ct) => ValueTask.FromResult<TelegramCredential?>(null);
}
internal sealed class TelegramCredential(string value, bool synthetic = false, long? configurationVersion = null,
    long? credentialGeneration = null, Guid? credentialId = null)
{
    internal string Value { get; } = value;
    internal bool Synthetic { get; } = synthetic;
    internal long? ConfigurationVersion { get; } = configurationVersion;
    internal long? CredentialGeneration { get; } = credentialGeneration;
    internal Guid? CredentialId { get; } = credentialId;
    public override string ToString() => "[REDACTED]";
}
internal sealed class TelegramFailure(string code, int retrySeconds = 0) : Exception(code)
{
    internal string Code { get; } = code;
    internal int RetrySeconds { get; } = retrySeconds;
}

// No automatic HTTP retry: replay/offset/backoff belong to the durable per-account coordinator.
internal sealed class TelegramApiClient(HttpClient http, TelegramProviderAdmission admission)
{
    internal async Task<JsonElement> ReadAsync(TelegramCredential credential, string method, long offset, CancellationToken ct)
    {
        if (!admission.Allowed || credential.Synthetic != admission.Synthetic) throw new TelegramFailure("LIVE_NOT_ADMITTED");
        if (http.BaseAddress is null || (admission.Synthetic ? !http.BaseAddress.IsLoopback
            : http.BaseAddress != new Uri("https://api.telegram.org/"))) throw new TelegramFailure("TRANSPORT_NOT_ADMITTED");
        if (method is not ("getMe" or "getWebhookInfo" or "getUpdates")) throw new TelegramFailure("METHOD_DENIED");
        if (offset < 0) throw new TelegramFailure("OFFSET_INVALID");
        if (credential.Value.Length is < 1 or > 256 || credential.Value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is ':' or '_' or '-')))
            throw new TelegramFailure("CREDENTIAL_INVALID");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(18));
        // Isolate token-bearing URL from Activity baggage; default HTTP diagnostics/loggers are disabled at registration.
        var previous = Activity.Current;
        Activity.Current = null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"bot{credential.Value}/{method}");
            if (method == "getUpdates") request.Content = JsonContent.Create(new { offset, limit = 100, timeout = 10, allowed_updates = Array.Empty<string>() });
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            var status = (int)response.StatusCode;
            if (status is 401 or 403) throw new TelegramFailure("REAUTH_REQUIRED");
            if (status == 409) throw new TelegramFailure("POLLING_CONFLICT");
            if (status >= 500) throw new TelegramFailure("PROVIDER_TRANSIENT");
            var headerRetry = response.Headers.RetryAfter?.Delta?.TotalSeconds;
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var buffer = new MemoryStream();
            var block = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(block, deadline.Token)) > 0)
            {
                if (buffer.Length + count > 2_000_000) throw new TelegramFailure("RESPONSE_INVALID");
                buffer.Write(block, 0, count);
            }
            using var document = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement;
            RejectDuplicates(root);
            if (root.ValueKind != JsonValueKind.Object) throw new TelegramFailure("RESPONSE_INVALID");
            var rateLimited = status == 429;
            if (root.TryGetProperty("error_code", out var error))
            {
                if (error.ValueKind != JsonValueKind.Number || !error.TryGetInt32(out var code)) throw new TelegramFailure("RESPONSE_INVALID");
                rateLimited |= code == 429;
            }
            if (rateLimited)
            {
                var retry = headerRetry ?? 30;
                if (root.TryGetProperty("parameters", out var parameters))
                {
                    if (parameters.ValueKind != JsonValueKind.Object) throw new TelegramFailure("RESPONSE_INVALID");
                    if (parameters.TryGetProperty("retry_after", out var value))
                    {
                        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var seconds)) throw new TelegramFailure("RESPONSE_INVALID");
                        retry = seconds;
                    }
                }
                throw new TelegramFailure("RATE_LIMITED", (int)Math.Clamp(retry, 1, 86400));
            }
            if (!response.IsSuccessStatusCode || !root.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True
                || !root.TryGetProperty("result", out var result)) throw new TelegramFailure("RESPONSE_INVALID");
            return result.Clone();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TelegramFailure("TIMEOUT"); }
        catch (HttpRequestException) { throw new TelegramFailure("NETWORK"); }
        catch (JsonException) { throw new TelegramFailure("RESPONSE_INVALID"); }
        finally { Activity.Current = previous; }
    }

    private static void RejectDuplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!keys.Add(property.Name)) throw new TelegramFailure("RESPONSE_INVALID");
                RejectDuplicates(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) RejectDuplicates(item);
    }
}
