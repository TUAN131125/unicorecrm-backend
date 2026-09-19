using System.Net;
using System.Text;

namespace UnicoreCRM.AI.Providers;

internal sealed class DevelopmentDeterministicProviderHttpHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        var gemini = request.RequestUri?.AbsolutePath.Contains("generateContent", StringComparison.Ordinal) == true;
        if (gemini && body.Contains("DETERMINISTIC_GEMINI_TRANSIENT_FAILURE", StringComparison.Ordinal))
            return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        var json = gemini
            ? "{\"responseId\":\"development-gemini-request\",\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"{\\\"summary\\\":\\\"Provider configuration verified.\\\",\\\"suggestedNextAction\\\":null,\\\"attentionPoints\\\":[]}\"}]}}],\"usageMetadata\":{\"promptTokenCount\":1,\"candidatesTokenCount\":1}}"
            : "{\"id\":\"development-openai-response\",\"status\":\"completed\",\"output\":[{\"content\":[{\"type\":\"output_text\",\"text\":\"{\\\"summary\\\":\\\"Provider configuration verified.\\\",\\\"suggestedNextAction\\\":null,\\\"attentionPoints\\\":[]}\"}]}],\"usage\":{\"input_tokens\":1,\"output_tokens\":1}}";
        return new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
