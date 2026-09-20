namespace UnicoreCRM.Crm.Customers.Domain.Health;

internal static class CustomerHealthVocabulary
{
    internal const string AlgorithmVersion = "CUSTOMER_HEALTH_PURCHASE_RECENCY_V1";
    internal const int DefaultCadenceDays = 90;
}

internal sealed record CustomerHealthInput(
    int PurchaseCount,
    IReadOnlyList<DateTimeOffset> RecentPurchaseTimestamps);

internal sealed record CustomerHealthResult(
    int? Score,
    string HealthBand,
    string ChurnRisk,
    string Confidence,
    int PurchaseCount,
    DateTimeOffset? LastPurchaseAt,
    int? ExpectedPurchaseCadenceDays,
    int? DaysSinceLastPurchase,
    string ReasonCode,
    string AlgorithmVersion,
    DateTimeOffset EvaluatedAt);

internal static class CustomerHealthCalculator
{
    internal static CustomerHealthResult Calculate(CustomerHealthInput input, DateTimeOffset asOf)
    {
        var evaluatedAt = asOf.ToUniversalTime();
        var valid = input.RecentPurchaseTimestamps
            .Select(value => value.ToUniversalTime())
            .Where(value => value <= evaluatedAt)
            .OrderBy(value => value)
            .ToArray();
        var purchaseCount = Math.Max(0, input.PurchaseCount - (input.RecentPurchaseTimestamps.Count - valid.Length));
        if (purchaseCount == 0 || valid.Length == 0)
            return new(null, "UNKNOWN", "UNKNOWN", "NONE", 0, null, null, null,
                "NO_PURCHASE_EVIDENCE", CustomerHealthVocabulary.AlgorithmVersion, evaluatedAt);

        var cadence = ResolveCadence(valid, purchaseCount);
        var lastPurchaseAt = valid[^1];
        var daysSinceLastPurchase = Math.Max(0, (int)Math.Floor((evaluatedAt - lastPurchaseAt).TotalDays));
        var ratio = (double)daysSinceLastPurchase / cadence;
        var score = Score(ratio);
        var band = score switch
        {
            >= 80 => "HEALTHY",
            >= 60 => "WATCH",
            >= 30 => "AT_RISK",
            _ => "CRITICAL"
        };
        var risk = band switch
        {
            "HEALTHY" => "LOW",
            "WATCH" => "MEDIUM",
            "AT_RISK" => "HIGH",
            _ => "CRITICAL"
        };
        var confidence = purchaseCount switch { 1 => "LOW", 2 => "MEDIUM", _ => "HIGH" };
        var reason = ratio switch
        {
            <= 0.75 => "PURCHASE_RECENCY_HEALTHY",
            <= 1.0 => "PURCHASE_WITHIN_EXPECTED_CADENCE",
            <= 1.5 => "PURCHASE_CADENCE_SLIPPING",
            <= 2.0 => "PURCHASE_OVER_EXPECTED_CADENCE",
            _ => "PURCHASE_SEVERELY_OVERDUE"
        };
        return new(score, band, risk, confidence, purchaseCount, lastPurchaseAt, cadence,
            daysSinceLastPurchase, reason, CustomerHealthVocabulary.AlgorithmVersion, evaluatedAt);
    }

    private static int ResolveCadence(IReadOnlyList<DateTimeOffset> timestamps, int purchaseCount)
    {
        if (purchaseCount < 3 || timestamps.Count < 3) return CustomerHealthVocabulary.DefaultCadenceDays;
        var intervals = timestamps.Zip(timestamps.Skip(1), (left, right) =>
                (int)Math.Floor((right - left).TotalDays))
            .Where(days => days > 0)
            .Order()
            .ToArray();
        if (intervals.Length < 2) return CustomerHealthVocabulary.DefaultCadenceDays;
        var middle = intervals.Length / 2;
        var median = intervals.Length % 2 == 1
            ? intervals[middle]
            : (int)Math.Round((intervals[middle - 1] + intervals[middle]) / 2d, MidpointRounding.AwayFromZero);
        return Math.Clamp(median, 7, 365);
    }

    private static int Score(double ratio)
    {
        var raw = ratio switch
        {
            <= 0.75 => 100d,
            <= 1.0 => Interpolate(ratio, 0.75, 1.0, 100, 85),
            <= 1.25 => Interpolate(ratio, 1.0, 1.25, 85, 70),
            <= 1.5 => Interpolate(ratio, 1.25, 1.5, 70, 50),
            <= 2.0 => Interpolate(ratio, 1.5, 2.0, 50, 25),
            <= 3.0 => Interpolate(ratio, 2.0, 3.0, 25, 0),
            _ => 0d
        };
        return Math.Clamp((int)Math.Round(raw, MidpointRounding.AwayFromZero), 0, 100);
    }

    private static double Interpolate(double value, double from, double to, double start, double end) =>
        start + ((value - from) / (to - from) * (end - start));
}
