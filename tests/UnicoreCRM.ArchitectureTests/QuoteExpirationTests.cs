using UnicoreCRM.Sales.Quotes.Application.Common;
using Xunit;

namespace UnicoreCRM.ArchitectureTests;

public sealed class QuoteExpirationTests
{
    [Theory]
    [InlineData("2026-10-25T16:59:59.9999999Z", "VALID", "2026-10-25")]
    [InlineData("2026-10-25T17:00:00Z", "OVERDUE", "2026-10-26")]
    [InlineData("2026-10-25T00:00:00Z", "VALID", "2026-10-25")]
    public void Valid_until_includes_the_entire_workspace_day(string utc, string state, string localDate)
    {
        var result = QuoteValidity.Evaluate(new DateOnly(2026, 10, 25), "Asia/Ho_Chi_Minh", DateTimeOffset.Parse(utc));
        Assert.Equal(state, result.State); Assert.Equal(localDate, result.LocalDate);
    }

    [Fact]
    public void Timezone_and_absent_expiry_are_not_guessed()
    {
        var now = DateTimeOffset.Parse("2026-10-25T00:00:00Z");
        Assert.Equal("TIMEZONE_UNAVAILABLE", QuoteValidity.Evaluate(new DateOnly(2026, 10, 24), null, now).State);
        Assert.Equal("TIMEZONE_UNAVAILABLE", QuoteValidity.Evaluate(null, "not-a-timezone", now).State);
        Assert.Equal("NO_EXPIRY", QuoteValidity.Evaluate(null, "Asia/Ho_Chi_Minh", now).State);
    }

    [Fact]
    public void Expiration_preserves_commercial_and_human_evidence()
    {
        var quote = QuoteRevisionTests.Source();
        var fingerprint = QuoteApprovalAssessment.ContentFingerprint(quote);
        var delivery = quote.DeliveryHistoryJson; var version = quote.ResourceVersion;
        Assert.False(quote.Expire(new DateOnly(2026, 10, 25), DateTimeOffset.Parse("2026-10-25T16:59:59Z")));
        Assert.True(quote.Expire(new DateOnly(2026, 10, 26), DateTimeOffset.Parse("2026-10-25T17:00:00Z")));
        Assert.Equal("EXPIRED", quote.Status); Assert.Equal(version + 1, quote.ResourceVersion);
        Assert.Equal(delivery, quote.DeliveryHistoryJson); Assert.Null(quote.RejectedAt); Assert.Null(quote.AcceptedAt);
        Assert.Equal(fingerprint, QuoteApprovalAssessment.ContentFingerprint(quote));
        Assert.False(quote.Expire(new DateOnly(2026, 10, 27), DateTimeOffset.Parse("2026-10-26T17:00:00Z")));
    }

    [Theory]
    [InlineData("DRAFT")]
    [InlineData("REVIEW")]
    [InlineData("ACCEPTED")]
    [InlineData("REJECTED")]
    [InlineData("EXPIRED")]
    public void Only_sent_can_expire(string status)
    {
        var quote = QuoteRevisionTests.Source(); QuoteRevisionTests.Set(quote, "Status", status);
        Assert.False(quote.Expire(new DateOnly(2026, 10, 26), DateTimeOffset.Parse("2026-10-25T17:00:00Z")));
    }
}
