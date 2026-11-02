using System.Net;
using System.Text;
using System.Text.Json;

namespace UnicoreCRM.AI.Providers;

internal sealed class DevelopmentDeterministicProviderHttpHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        var gemini = request.RequestUri?.AbsolutePath.Contains("generateContent", StringComparison.Ordinal) == true;
        using var payload = JsonDocument.Parse(body);
        var prompt = gemini ? payload.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString() ?? ""
            : payload.RootElement.GetProperty("input")[1].GetProperty("content").GetString() ?? "";
        var instruction = prompt.Split("<untrusted_conversation_history>", 2, StringSplitOptions.None)[0];
        var credential = gemini ? request.Headers.GetValues("x-goog-api-key").Single() : request.Headers.Authorization?.Parameter;
        if (credential == "deterministic-invalid-key")
            return new(HttpStatusCode.Unauthorized) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        if (instruction.Contains("DETERMINISTIC_RATE_LIMIT", StringComparison.Ordinal))
            return new(HttpStatusCode.TooManyRequests) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        if (gemini && instruction.Contains("DETERMINISTIC_GEMINI_TRANSIENT_FAILURE", StringComparison.Ordinal))
            return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        var json = gemini
            ? "{\"responseId\":\"development-gemini-request\",\"candidates\":[{\"finishReason\":\"STOP\",\"content\":{\"parts\":[{\"text\":\"{\\\"summary\\\":\\\"Provider configuration verified.\\\",\\\"suggestedNextAction\\\":null,\\\"attentionPoints\\\":[]}\"}]}}],\"usageMetadata\":{\"promptTokenCount\":1,\"candidatesTokenCount\":1}}"
            : "{\"id\":\"development-openai-response\",\"status\":\"completed\",\"output\":[{\"content\":[{\"type\":\"output_text\",\"text\":\"{\\\"summary\\\":\\\"Provider configuration verified.\\\",\\\"suggestedNextAction\\\":null,\\\"attentionPoints\\\":[]}\"}]}],\"usage\":{\"input_tokens\":1,\"output_tokens\":1}}";
        return new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
