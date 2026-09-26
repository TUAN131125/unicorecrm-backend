using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace UnicoreCRM.AI.Providers;

internal sealed class OpenAiProvider(HttpClient httpClient, TimeProvider timeProvider) : IProductionAiProviderAdapter
{
    public string ProviderId => "OPENAI";

    public async Task<AiProviderAdapterResult> CompleteAsync(string model, string credential, AiProviderRequest request, CancellationToken cancellationToken)
    {
        var started = timeProvider.GetTimestamp();
        using var message = new HttpRequestMessage(HttpMethod.Post, "v1/responses");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        message.Content = JsonContent.Create(new
        {
            model, store = false, max_output_tokens = 2048,
            input = new[] { new { role = "system", content = request.SystemInstruction }, new { role = "user", content = request.UserInstruction + "\n" + request.ContextData } },
            text = new { format = new { type = "json_schema", name = request.OutputContract == AiProviderOutputContract.ProactiveSuggestion ? "crm_proactive_suggestion" : "crm_advisory", strict = true, schema = request.OutputContract == AiProviderOutputContract.ProactiveSuggestion ? (object)ProactiveSuggestionOutputValidator.Schema : new
            {
                type = "object", additionalProperties = false, required = new[] { "summary", "suggestedNextAction", "attentionPoints" }, properties = new
                {
                    summary = new { type = "string" }, suggestedNextAction = new { type = new[] { "string", "null" } },
                    attentionPoints = new { type = "array", maxItems = 5, items = new { type = "string" } }
                }
            } } }
        });
        try
        {
            using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode) throw ProviderHttpFailureMapper.FromStatus(response.StatusCode);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = json.RootElement;
            if (root.TryGetProperty("status", out var status) && status.GetString() == "incomplete")
                throw new AiProviderExecutionException(AiProviderFailure.InvalidResponse, "Provider response was incomplete.");
            if (!TryOutputText(root, out var content)) throw new AiProviderExecutionException(AiProviderFailure.InvalidResponse, "Provider response content was malformed.");
            var usage = root.TryGetProperty("usage", out var usageElement) ? usageElement : default;
            return new(ProviderId, model, content!, timeProvider.GetElapsedTime(started),
                response.Headers.TryGetValues("x-request-id", out var ids) ? ids.FirstOrDefault() : root.TryGetProperty("id", out var id) ? id.GetString() : null,
                Int(usage, "input_tokens"), Int(usage, "output_tokens"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (HttpRequestException exception) { throw new AiProviderExecutionException(AiProviderFailure.NetworkFailure, "Provider network request failed.", exception); }
        catch (JsonException exception) { throw new AiProviderExecutionException(AiProviderFailure.InvalidResponse, "Provider response was not valid JSON.", exception); }
    }

    private static bool TryOutputText(JsonElement root, out string? text)
    {
        text = null; if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array) return false;
        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var contents) || contents.ValueKind != JsonValueKind.Array) continue;
            foreach (var content in contents.EnumerateArray())
            {
                if (content.TryGetProperty("type", out var type) && type.GetString() == "refusal")
                    throw new AiProviderExecutionException(AiProviderFailure.SafetyRefusal, "Provider safety policy refused the request.");
                if (content.TryGetProperty("type", out type) && type.GetString() == "output_text" && content.TryGetProperty("text", out var value))
                { text = value.GetString(); if (!string.IsNullOrWhiteSpace(text)) return true; }
            }
        }
        return false;
    }
    private static int? Int(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : null;
}
