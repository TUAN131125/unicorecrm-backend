using System.Text.Json;
using System.Text.Json.Serialization;
using UnicoreCRM.Crm.Deals.Application.Common;
using UnicoreCRM.Crm.Deals.Application.CreateDeal;
using UnicoreCRM.Crm.Deals.Contracts;
using UnicoreCRM.Crm.Deals.Domain;
using Xunit;

namespace UnicoreCRM.ArchitectureTests;

public sealed class DealOptionalEstimateTests
{
    [Theory]
    [InlineData(null, false, true)]
    [InlineData("0", false, true)]
    [InlineData(null, true, false)]
    [InlineData("0", true, false)]
    [InlineData("0.000001", true, true)]
    public void Unknown_and_zero_estimates_obey_the_configured_stage(string? amount, bool positive, bool allowed)
    {
        var profile = new DealProfile("Opportunity", new("CONTACT", "contact_a"),
            amount is null ? null : new(amount, "VND"), "10", "member_a", new(2026, 12, 2), null, null, [], null);
        var stage = new DealPipelineStage("stage_a", "CUSTOM", "Tư vấn", "Advisory", 0,
            DealStageCategory.Open, 10, "teal", true, false, requiresPositiveAmount: positive);
        Assert.Equal(allowed, !DealValidation.ProgressiveProfileErrors(profile, stage, DealForecastCategory.Pipeline).ContainsKey("amount"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    public void Create_and_persisted_projection_preserve_unknown_separately_from_zero(string? amount)
    {
        var input = new CreateDealRequest("Opportunity", new("CONTACT", "contact_a"), null,
            amount is null ? null : new(amount, "VND"), "10", "member_a", "2026-12-02", [], []);
        Assert.True(CreateDealValidation.TryCreate(input, out var profile, out _, out _, out _, out _, out _, out var errors));
        Assert.Empty(errors);
        var deal = new Deal("ws_a", profile!, "DISCOVERY", DealStageCategory.Open, DealForecastCategory.Pipeline, DateTimeOffset.UtcNow);
        var json = JsonSerializer.SerializeToElement(DealProjection.Document(deal), new JsonSerializerOptions(JsonSerializerDefaults.Web)
        { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
        Assert.True(json.TryGetProperty("amount", out var value));
        if (amount is null) Assert.Equal(JsonValueKind.Null, value.ValueKind);
        else Assert.Equal(amount, value.GetProperty("amount").GetString());
    }

    [Theory]
    [InlineData("-1", "VND")]
    [InlineData("1.1234567", "VND")]
    [InlineData("", "VND")]
    [InlineData("1", "vnd")]
    public void Supplied_invalid_values_stay_invalid(string amount, string currency)
    {
        var errors = new Dictionary<string, string[]>();
        Assert.Null(DealValidation.Money(new(amount, currency), "amount", errors));
        Assert.NotEmpty(errors);
    }
}
