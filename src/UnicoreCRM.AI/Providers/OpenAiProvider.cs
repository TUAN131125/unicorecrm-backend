using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnicoreCRM.AI.Providers;

internal sealed class OpenAiProvider(HttpClient httpClient, TimeProvider timeProvider) : IProductionAiProviderAdapter
{
    public string ProviderId => "OPENAI";

    public async Task<AiProviderAdapterResult> CompleteAsync(string model, string credential, AiProviderRequest request, CancellationToken cancellationToken)
    {
        var started = timeProvider.GetTimestamp();
        int? providerHttpStatus = null;
        using var message = new HttpRequestMessage(HttpMethod.Post, "v1/responses");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        message.Content = JsonContent.Create(new
        {
            model, store = false, max_output_tokens = 2048,
            // Keep the documented medium reasoning baseline for Chat. Only the
            // synthetic health check lowers effort; legacy model behavior stays unchanged.
            reasoning = AiProviderCatalog.IsCurrentOpenAiModel(model)
                ? new { effort = request.IsConfigurationTest ? "low" : "medium" } : null,
            input = new[] { new { role = "system", content = request.SystemInstruction }, new { role = "user", content = request.UserInstruction + "\n" + request.ContextData } },
            text = new { format = new { type = "json_schema", name = request.OutputContract == AiProviderOutputContract.ProactiveSuggestion ? "crm_proactive_suggestion" : "crm_advisory", strict = true, schema = request.OutputContract == AiProviderOutputContract.ProactiveSuggestion ? (object)ProactiveSuggestionOutputValidator.Schema : new
            {
                type = "object", additionalProperties = false, required = new[] { "summary", "suggestedNextAction", "attentionPoints" }, properties = new
                {
                    summary = new { type = "string" }, suggestedNextAction = new { type = new[] { "string", "null" } },
                    attentionPoints = new { type = "array", maxItems = 5, items = new { type = "string" } }
                }
            } } }
        }, options: new JsonSerializerOptions(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
        try
        {
            using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            providerHttpStatus = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode) throw ProviderHttpFailureMapper.FromStatus(response.StatusCode);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("status", out var status)
                || status.ValueKind != JsonValueKind.String || status.GetString() != "completed")
                throw new AiProviderExecutionException(AiProviderFailure.InvalidResponse, "Provider response was not completed.") { ProviderHttpStatus = providerHttpStatus };
            if (!TryOutputText(root, out var content)) throw new AiProviderExecutionException(AiProviderFailure.InvalidResponse, "Provider response content was malformed.") { ProviderHttpStatus = providerHttpStatus };
            var usage = root.TryGetProperty("usage", out var usageElement) ? usageElement : default;
            return new(ProviderId, model, content!, timeProvider.GetElapsedTime(started),
                response.Headers.TryGetValues("x-request-id", out var ids) ? ids.FirstOrDefault() : root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null,
                Int(usage, "input_tokens"), Int(usage, "output_tokens"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException exception) when (exception.InnerException is TimeoutException)
        {
            throw new AiProviderExecutionException(AiProviderFailure.Timeout, "Provider HTTP client deadline elapsed.")
                { TimeoutSource = AiProviderTimeoutSource.HttpClientDeadline, ProviderHttpStatus = providerHttpStatus };
        }
        catch (HttpRequestException exception) { throw new AiProviderExecutionException(AiProviderFailure.NetworkFailure, "Provider network request failed.", exception) { NetworkError = exception.HttpRequestError, ProviderHttpStatus = providerHttpStatus }; }
        catch (IOException exception) { throw new AiProviderExecutionException(AiProviderFailure.NetworkFailure, "Provider network response could not be read.", exception) { ProviderHttpStatus = providerHttpStatus }; }
        catch (JsonException exception) { throw new AiProviderExecutionException(AiProviderFailure.InvalidResponse, "Provider response was not valid JSON.", exception) { ProviderHttpStatus = providerHttpStatus }; }
    }

    private static bool TryOutputText(JsonElement root, out string? text)
    {
        text = null; if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array) return false;
        var buffer = new StringBuilder();
        foreach (var item in output.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) return false;
            if (item.TryGetProperty("status", out var itemStatus) && (itemStatus.ValueKind != JsonValueKind.String || itemStatus.GetString() != "completed")) return false;
            if (!item.TryGetProperty("content", out var contents) || contents.ValueKind != JsonValueKind.Array) continue;
            foreach (var content in contents.EnumerateArray())
            {
                if (content.ValueKind != JsonValueKind.Object || !content.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String) return false;
                if (type.GetString() == "refusal")
                    throw new AiProviderExecutionException(AiProviderFailure.SafetyRefusal, "Provider safety policy refused the request.");
                if (type.GetString() == "output_text")
                {
                    if (!content.TryGetProperty("text", out var value) || value.ValueKind != JsonValueKind.String) return false;
                    buffer.Append(value.GetString());
                }
            }
        }
        text = buffer.ToString();
        return !string.IsNullOrWhiteSpace(text);
    }
    private static int? Int(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result) ? result : null;
}
