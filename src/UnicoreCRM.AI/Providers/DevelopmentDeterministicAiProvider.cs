using System.Text.Json;

namespace UnicoreCRM.AI.Providers;

internal sealed class DevelopmentDeterministicAiProvider(string mode, TimeSpan attemptTimeout) : IAiProvider
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
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeout.CancelAfter(attemptTimeout);
                    await Task.Delay(Timeout.InfiniteTimeSpan, timeout.Token);
                }
                throw new InvalidOperationException("Unreachable after cancellation.");
            case "MALFORMED":
                return new AiProviderResponse("{\"summary\":\"\",\"suggestedNextAction\":42}");
            case "RATE_LIMITED":
                throw new AiProviderRateLimitedException();
        }

        var vietnamese = string.Equals(request.Locale, "vi", StringComparison.Ordinal);
        if (request.OutputContract == AiProviderOutputContract.ProactiveSuggestion)
            return new(JsonSerializer.Serialize(new
            {
                summary = vietnamese ? "Đã xem xét tình trạng sức khỏe khách hàng được cung cấp." : "Reviewed the supplied Customer Health context.",
                suggestedNextStep = vietnamese ? "Xem xét liên hệ để tìm hiểu nhu cầu hiện tại." : "Consider contacting the customer to understand current needs.",
                taskDraft = new
                {
                    title = vietnamese ? "Theo dõi khách hàng" : "Follow up with the customer",
                    description = vietnamese ? "Tìm hiểu nhu cầu hiện tại và ghi nhận phản hồi." : "Understand current needs and record feedback."
                }
            }));
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
