using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace UnicoreCRM.AI.Providers;

internal sealed class GeminiAiProvider(HttpClient httpClient, TimeProvider timeProvider) : IProductionAiProviderAdapter
{
    public string ProviderId => "GEMINI";

    public async Task<AiProviderAdapterResult> CompleteAsync(string model, string credential, AiProviderRequest request, CancellationToken cancellationToken)
    {
        var started = timeProvider.GetTimestamp();
        using var message = new HttpRequestMessage(HttpMethod.Post, $"v1beta/models/{Uri.EscapeDataString(model)}:generateContent");
        message.Headers.Add("x-goog-api-key", credential);
        message.Content = JsonContent.Create(new
        {
            systemInstruction = new { parts = new[] { new { text = request.SystemInstruction } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = request.UserInstruction + "\n" + request.ContextData } } } },
            generationConfig = new
            {
                maxOutputTokens = 2048,
                responseMimeType = "application/json",
                responseSchema = request.OutputContract == AiProviderOutputContract.ProactiveSuggestion ? (object)new
                {
                    type = "object", required = new[] { "summary", "suggestedNextStep", "taskDraft" }, properties = new
                    {
                        summary = new { type = "string" }, suggestedNextStep = new { type = "string" },
                        taskDraft = new { type = "object", required = new[] { "title", "description" }, properties = new
                        { title = new { type = "string" }, description = new { type = "string" } } }
                    }
                } : new { type = "object", required = new[] { "summary", "attentionPoints" }, properties = new
                {
                    summary = new { type = "string" }, suggestedNextAction = new { type = "string", nullable = true },
                    attentionPoints = new { type = "array", maxItems = 5, items = new { type = "string" } }
                } }
            }
        });
        try
        {
            using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode) throw ProviderHttpFailureMapper.FromStatus(response.StatusCode);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = json.RootElement;
            if (root.TryGetProperty("promptFeedback", out var feedback) && feedback.TryGetProperty("blockReason", out var blockReason)
                && !string.IsNullOrWhiteSpace(blockReason.GetString()))
                throw new AiProviderExecutionException(AiProviderFailure.SafetyRefusal, "Provider safety policy refused the request.");
            if (!TryText(root, out var content)) throw new AiProviderExecutionException(AiProviderFailure.InvalidResponse, "Provider response content was malformed.");
            var usage = root.TryGetProperty("usageMetadata", out var usageMetadata) ? usageMetadata : default;
            return new(ProviderId, model, content!, timeProvider.GetElapsedTime(started),
                root.TryGetProperty("responseId", out var responseId) ? responseId.GetString() : Header(response, "x-request-id"),
                Int(usage, "promptTokenCount"), Int(usage, "candidatesTokenCount"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (HttpRequestException exception) { throw new AiProviderExecutionException(AiProviderFailure.NetworkFailure, "Provider network request failed.", exception); }
        catch (JsonException exception) { throw new AiProviderExecutionException(AiProviderFailure.InvalidResponse, "Provider response was not valid JSON.", exception); }
    }

    private static bool TryText(JsonElement root, out string? text)
    {
        text = null;
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0) return false;
        var candidate = candidates[0];
        if (candidate.TryGetProperty("finishReason", out var reason) && reason.GetString() is "SAFETY" or "PROHIBITED_CONTENT")
            throw new AiProviderExecutionException(AiProviderFailure.SafetyRefusal, "Provider safety policy refused the response.");
        if (!candidate.TryGetProperty("content", out var content) || !content.TryGetProperty("parts", out var parts) || parts.GetArrayLength() == 0
            || !parts[0].TryGetProperty("text", out var value)) return false;
        text = value.GetString(); return !string.IsNullOrWhiteSpace(text);
    }
    private static int? Int(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : null;
    private static string? Header(HttpResponseMessage response, string name) => response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
}
