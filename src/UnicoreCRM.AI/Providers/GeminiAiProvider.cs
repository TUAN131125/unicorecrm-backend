using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace UnicoreCRM.AI.Providers;

internal sealed class GeminiAiProvider(HttpClient httpClient, TimeProvider timeProvider, ILogger<GeminiAiProvider>? logger = null) : IProductionAiProviderAdapter
{
    public string ProviderId => "GEMINI";

    public async Task<AiProviderAdapterResult> CompleteAsync(string model, string credential, AiProviderRequest request, CancellationToken cancellationToken)
    {
        var started = timeProvider.GetTimestamp();
        var phase = "SEND_OR_WAIT_HEADERS";
        int? providerHttpStatus = null;
        using var message = new HttpRequestMessage(HttpMethod.Post, $"v1beta/models/{Uri.EscapeDataString(model)}:generateContent");
        message.Headers.Add("x-goog-api-key", credential);
        message.Content = JsonContent.Create(new
        {
            systemInstruction = new { parts = new[] { new { text = request.SystemInstruction } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = request.UserInstruction + "\n" + request.ContextData } } } },
            generationConfig = new
            {
                // Gemini counts thinking tokens in this budget too. Reject truncation below;
                // only the synthetic 3.8 health check opts into lower thinking.
                maxOutputTokens = 2048,
                responseMimeType = "application/json",
                thinkingConfig = request.IsConfigurationTest && model == "gemini-3.8-flash" ? new { thinkingLevel = "low" } : null,
                responseSchema = request.OutputContract == AiProviderOutputContract.ProactiveSuggestion ? (object)new
                {
                    type = "OBJECT", required = new[] { "summary", "suggestedNextStep", "taskDraft" }, properties = new
                    {
                        summary = new { type = "STRING" }, suggestedNextStep = new { type = "STRING" },
                        taskDraft = new { type = "OBJECT", required = new[] { "title", "description" }, properties = new
                        { title = new { type = "STRING" }, description = new { type = "STRING" } } }
                    }
                } : new { type = "OBJECT", required = new[] { "summary", "attentionPoints" }, properties = new
                {
                    summary = new { type = "STRING" }, suggestedNextAction = new { type = "STRING", nullable = true },
                    attentionPoints = new { type = "ARRAY", maxItems = "5", items = new { type = "STRING" } }
                } }
            }
        }, options: new JsonSerializerOptions(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
        try
        {
            if (request.IsConfigurationTest) logger?.LogInformation("Gemini test transport starting. CorrelationId={CorrelationId} Phase={Phase}", request.CorrelationId, phase);
            using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            providerHttpStatus = (int)response.StatusCode;
            phase = "READ_RESPONSE";
            if (request.IsConfigurationTest) logger?.LogInformation("Gemini test response headers received. CorrelationId={CorrelationId} ProviderHttpStatus={ProviderHttpStatus} ElapsedMs={ElapsedMs}",
                request.CorrelationId, providerHttpStatus, (long)timeProvider.GetElapsedTime(started).TotalMilliseconds);
            if (!response.IsSuccessStatusCode) throw await ProviderHttpFailureMapper.FromGeminiAsync(response, cancellationToken);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw InvalidEnvelope(providerHttpStatus.Value);
            if (root.TryGetProperty("promptFeedback", out var feedback) && feedback.ValueKind == JsonValueKind.Object
                && feedback.TryGetProperty("blockReason", out var blockReason) && blockReason.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(blockReason.GetString()))
                throw new AiProviderExecutionException(AiProviderFailure.SafetyRefusal, "Provider safety policy refused the request.") { ProviderHttpStatus = providerHttpStatus };
            var content = ReadText(root, providerHttpStatus.Value);
            var usage = root.TryGetProperty("usageMetadata", out var usageMetadata) ? usageMetadata : default;
            return new(ProviderId, model, content, timeProvider.GetElapsedTime(started),
                root.TryGetProperty("responseId", out var responseId) && responseId.ValueKind == JsonValueKind.String ? responseId.GetString() : Header(response, "x-request-id"),
                Int(usage, "promptTokenCount"), Int(usage, "candidatesTokenCount"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (request.IsConfigurationTest) logger?.LogWarning("Gemini test transport cancelled. CorrelationId={CorrelationId} Phase={Phase} ProviderHttpStatus={ProviderHttpStatus} ElapsedMs={ElapsedMs}",
                request.CorrelationId, phase, providerHttpStatus, (long)timeProvider.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
        catch (OperationCanceledException exception) when (exception.InnerException is TimeoutException)
        {
            throw new AiProviderExecutionException(AiProviderFailure.Timeout, "Provider HTTP client deadline elapsed.")
                { TimeoutSource = AiProviderTimeoutSource.HttpClientDeadline, ProviderHttpStatus = providerHttpStatus };
        }
        catch (HttpRequestException exception) { throw new AiProviderExecutionException(AiProviderFailure.NetworkFailure, "Provider network request failed.", exception) { NetworkError = exception.HttpRequestError }; }
        catch (IOException exception) { throw new AiProviderExecutionException(AiProviderFailure.NetworkFailure, "Provider network response could not be read.", exception); }
        catch (JsonException exception) { throw new AiProviderExecutionException(AiProviderFailure.InvalidResponse, "Provider response was not valid JSON.", exception) { ProviderHttpStatus = providerHttpStatus }; }
    }

    private static string ReadText(JsonElement root, int status)
    {
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0)
            throw InvalidEnvelope(status);
        var candidate = candidates[0];
        if (candidate.ValueKind != JsonValueKind.Object) throw InvalidEnvelope(status);
        var reason = candidate.TryGetProperty("finishReason", out var finish) && finish.ValueKind == JsonValueKind.String ? finish.GetString() : null;
        if (reason is "SAFETY" or "RECITATION" or "BLOCKLIST" or "PROHIBITED_CONTENT" or "SPII" or "IMAGE_SAFETY" or "IMAGE_PROHIBITED_CONTENT" or "IMAGE_RECITATION")
            throw new AiProviderExecutionException(AiProviderFailure.SafetyRefusal, "Provider safety policy refused the response.") { ProviderHttpStatus = status };
        // Unary structured responses must be complete. Even valid JSON from MAX_TOKENS
        // is truncated generation, and must never validate a configuration.
        if (reason != "STOP")
            throw new AiProviderExecutionException(AiProviderFailure.InvalidResponse, "Provider did not complete the structured response.")
            { ProviderHttpStatus = status, ProviderErrorCode = reason == "MAX_TOKENS" ? "MAX_TOKENS" : reason is null ? "FINISH_REASON_MISSING" : "FINISH_REASON_UNSUPPORTED" };
        if (!candidate.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Object
            || !content.TryGetProperty("parts", out var parts) || parts.ValueKind != JsonValueKind.Array || parts.GetArrayLength() == 0)
            throw InvalidEnvelope(status);
        var text = new StringBuilder();
        foreach (var part in parts.EnumerateArray())
        {
            if (part.ValueKind != JsonValueKind.Object) throw InvalidEnvelope(status);
            var thought = part.TryGetProperty("thought", out var thoughtValue);
            if (thought && thoughtValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw InvalidEnvelope(status);
            if (!part.TryGetProperty("text", out var value)) continue;
            if (value.ValueKind != JsonValueKind.String) throw InvalidEnvelope(status);
            if (!thought || !thoughtValue.GetBoolean()) text.Append(value.GetString());
        }
        // Do not insert separators: JSON can be split inside a string or token.
        var result = text.ToString();
        if (string.IsNullOrWhiteSpace(result)) throw InvalidEnvelope(status);
        return result;
    }
    private static AiProviderExecutionException InvalidEnvelope(int status) => new(AiProviderFailure.InvalidResponse, "Provider response content was malformed.") { ProviderHttpStatus = status };
    private static int? Int(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result) ? result : null;
    private static string? Header(HttpResponseMessage response, string name) => response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
}
