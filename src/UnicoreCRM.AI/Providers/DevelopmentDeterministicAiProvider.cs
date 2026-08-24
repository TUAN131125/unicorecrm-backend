using System.Text.Json;

namespace UnicoreCRM.AI.Providers;

internal sealed class DevelopmentDeterministicAiProvider(string mode) : IAiProvider
{
    public AiProviderDescriptor Descriptor { get; } =
        new("development-deterministic", "deterministic-advisory-v1");

    public async Task<AiProviderResponse> CompleteAsync(
        AiProviderRequest request,
        CancellationToken cancellationToken)
    {
        switch (mode.ToUpperInvariant())
        {
            case "UNAVAILABLE":
                throw new AiProviderUnavailableException();
            case "TIMEOUT":
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("Unreachable after cancellation.");
            case "MALFORMED":
                return new AiProviderResponse("{\"summary\":\"\",\"suggestedNextAction\":42}");
        }

        var vietnamese = string.Equals(request.Locale, "vi", StringComparison.Ordinal);
        var payload = new
        {
            summary = vietnamese
                ? $"Đã xem xét {request.ContextCount} bản ghi CRM được phép."
                : $"Reviewed {request.ContextCount} authorized CRM record(s).",
            suggestedNextAction = vietnamese
                ? "Xem lại các điểm cần chú ý và xác nhận bước theo dõi phù hợp."
                : "Review the attention points and confirm the appropriate follow-up.",
            attentionPoints = new[]
            {
                vietnamese
                    ? "Kết quả này chỉ là tư vấn và chưa thay đổi dữ liệu CRM."
                    : "This result is advisory and has not changed CRM state."
            }
        };
        return new AiProviderResponse(JsonSerializer.Serialize(payload));
    }
}
