using UnicoreCRM.PlatformOperations.AiExecution.Contracts;

namespace UnicoreCRM.AI.Gateway;

internal static class ProactiveWhyFormatter
{
    internal static ProactiveSuggestionWhy Format(ProactiveItemState item, string healthBand, string locale)
    {
        if (item.TriggerType != ProactiveValues.CustomerHealthRisk) throw new ArgumentException("Unsupported trigger.", nameof(item));
        var vi = locale == "vi";
        var reason = item.ReasonCode switch
        {
            "PURCHASE_CADENCE_SLIPPING" => vi ? "nhịp mua hàng đang chậm lại" : "purchase cadence was slipping",
            "PURCHASE_OVER_EXPECTED_CADENCE" => vi ? "thời gian mua hàng đã vượt nhịp dự kiến" : "purchase timing exceeded the expected cadence",
            "PURCHASE_SEVERELY_OVERDUE" => vi ? "thời gian mua hàng đã quá hạn đáng kể" : "purchase timing was severely overdue",
            _ => vi ? "đánh giá sức khỏe khách hàng đã phát hiện rủi ro" : "the Customer Health assessment detected risk"
        };
        var band = (healthBand, vi) switch
        {
            ("HEALTHY", true) => "khỏe mạnh", ("WATCH", true) => "cần theo dõi",
            ("AT_RISK", true) => "có rủi ro", ("CRITICAL", true) => "nghiêm trọng",
            ("UNKNOWN", true) => "chưa xác định", _ => healthBand
        };
        return new(item.ReasonCode, vi
            ? $"Mục chú ý được ghi nhận vì {reason}. Mức sức khỏe khách hàng hiện tại: {band}."
            : $"This attention item was recorded because {reason}. Current Customer Health band: {band}.");
    }
}
