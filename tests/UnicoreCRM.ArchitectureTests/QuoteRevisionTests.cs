using System.Reflection;
using System.Text.Json;
using UnicoreCRM.Sales.Quotes.Application.Common;
using UnicoreCRM.Sales.Quotes.Contracts;
using UnicoreCRM.Sales.Quotes.Domain;
using Xunit;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.ArchitectureTests;

public sealed class QuoteRevisionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-25T00:00:00Z");

    [Theory]
    [InlineData("SENT")]
    [InlineData("REJECTED")]
    [InlineData("EXPIRED")]
    public void Revision_is_a_new_draft_and_source_is_immutable(string status)
    {
        var source = Source(); Set(source, "Status", status);
        Set(source, "ApprovalStatus", "APPROVED"); Set(source, "ApprovalContentFingerprint", "prior-binding");
        var before = JsonSerializer.Serialize(QuoteProjection.Document(source), QuotePricing.Json);
        var content = QuoteRevisionContent.Build(source, "revision-new"); Assert.True(content.IsSuccess);
        var next = source.CreateRevision("revision-new", "Q-0000000999", 2, content.Value!, Now.AddMinutes(1));
        Assert.NotNull(next); Assert.Equal("DRAFT", next.Status); Assert.Equal(1, next.ResourceVersion);
        Assert.Equal(2, next.QuoteRevision); Assert.Equal(source.RootQuoteId, next.RootQuoteId);
        Assert.Equal(source.QuoteId, next.RevisionOfQuoteId); Assert.NotEqual(source.QuoteNumber, next.QuoteNumber);
        Assert.Null(next.ApprovalStatus); Assert.Null(next.ApprovalContentFingerprint); Assert.Null(next.DeliveryHistoryJson);
        Assert.Empty(next.CustomerDecisions); Assert.Null(next.SentAt); Assert.Null(next.AcceptedAt);
        Assert.Equal(source.ProductSnapshotsJson, next.ProductSnapshotsJson);
        Assert.Equal(source.GrandTotalAmount, next.GrandTotalAmount);
        Assert.Equal(before, JsonSerializer.Serialize(QuoteProjection.Document(source), QuotePricing.Json));
        Assert.NotEqual(QuoteApprovalAssessment.ContentFingerprint(source), QuoteApprovalAssessment.ContentFingerprint(next));
    }

    [Theory]
    [InlineData("DRAFT")]
    [InlineData("REVIEW")]
    [InlineData("ACCEPTED")]
    public void Invalid_source_states_cannot_be_revised(string status)
    {
        var source = Source(); Set(source, "Status", status);
        var content = QuoteRevisionContent.Build(source, "new").Value!;
        Assert.Null(source.CreateRevision("new", "Q-0000000999", 2, content, Now));
    }

    [Fact]
    public void Archived_source_and_non_increasing_revision_are_blocked()
    {
        var source = Source(); var content = QuoteRevisionContent.Build(source, "new").Value!;
        Assert.Null(source.CreateRevision("new", "Q-0000000999", 1, content, Now));
        Set(source, "ArchivedAt", Now);
        Assert.Null(source.CreateRevision("new", "Q-0000000999", 2, content, Now));
    }

    [Fact]
    public void Copied_sender_snapshot_requires_target_write_access()
    {
        var source = Source(); Set(source, "SenderName", "Historical seller");
        var fields = QuoteFieldSecurity.FieldKeys.ToDictionary(f => f, f => f == "senderName" ? RecordFieldEnforcement.ReadOnly : RecordFieldEnforcement.ReadWrite);
        var grant = new RecordAccessAuthorization(true, "AUTHORIZED", new("workspace", "account", "actor", "membership"),
            RecordAccessScopeFilter.Workspace, null, "WORKSPACE", fields, [], ["quotes.read", "quotes.create"], "policy", "quotes", "quotes.create", true);
        Assert.False(QuoteRevisionCommands.CanCreate(QuoteProjection.Document(source), grant));
        fields["senderName"] = RecordFieldEnforcement.ReadWrite;
        Assert.True(QuoteRevisionCommands.CanCreate(QuoteProjection.Document(source), grant));
    }

    [Fact]
    public void Shipping_read_snapshot_round_trips_to_draft_without_repricing()
    {
        var source = Source();
        var shipping = new CommercialAdjustmentReadModel("shipping", "Shipping", "SHIPPING", "FIXED_AMOUNT", "10", new("10", "USD"));
        Set(source, "AdjustmentsJson", JsonSerializer.Serialize(new[] { shipping }, QuotePricing.Json));
        Set(source, "GrandTotalAmount", 110m);
        var result = QuoteRevisionContent.Build(source, "shipping-revision");
        Assert.True(result.IsSuccess); Assert.Equal(110m, result.Value!.Total);
        var newShipping = JsonSerializer.Deserialize<CommercialAdjustmentReadModel[]>(result.Value.AdjustmentsJson, QuotePricing.Json)!;
        Assert.Equal("SHIPPING", Assert.Single(newShipping).Type); Assert.Equal("10", newShipping[0].Amount.Amount);
        Assert.Equal("SHIPPING", JsonSerializer.Deserialize<CommercialAdjustmentReadModel[]>(source.AdjustmentsJson!, QuotePricing.Json)![0].Type);
    }

    [Fact]
    public void Corrupt_snapshot_is_not_silently_repriced()
    {
        var source = Source(); Set(source, "GrandTotalAmount", 101m);
        Assert.Equal("SOURCE_SNAPSHOT_INVALID", QuoteRevisionContent.Build(source, "new").Error?.Detail);
    }

    [Fact]
    public void Payment_terms_rebind_to_new_quote_without_acceptance()
    {
        var source = Source();
        var agreement = new PaymentAgreementSnapshotDocument(7, "FULL_PAYMENT", "USD",
            [new("old-line", 1, "Full", "FULL", new("REMAINDER"), new("100", "USD"), new("EVENT_RELATIVE") { Event = "ORDER_CONFIRMED", OffsetDays = 0, DayBasis = "CALENDAR" }, ["BANK_TRANSFER"], "NONE")]);
        // Build from the actual constructor below rather than carrying accepted/source IDs into the revision.
        Set(source, "PaymentAgreementJson", JsonSerializer.Serialize(agreement with { AcceptedAt = Now.ToString("O"), SourceQuoteId = source.QuoteId }, QuotePricing.Json));
        var result = QuoteRevisionContent.Build(source, "new-payment-quote");
        Assert.True(result.IsSuccess);
        var next = JsonSerializer.Deserialize<PaymentAgreementSnapshotDocument>(result.Value!.PaymentAgreementJson!, QuotePricing.Json)!;
        Assert.Null(next.AcceptedAt); Assert.Equal("new-payment-quote", next.SourceQuoteId); Assert.Equal(1, next.Version);
        Assert.NotEqual(agreement.Lines[0].Id, next.Lines[0].Id);
        Assert.Equal(agreement.Lines[0].AmountRule, next.Lines[0].AmountRule);
        Assert.Equal(agreement.Lines[0].DueRule, next.Lines[0].DueRule);
    }

    internal static Quote Source()
    {
        var line = new QuoteLineReadModel("source-line", "Historical product", "1", new("100", "USD"), "0", "NONE",
            new("100", "USD"), new("0", "USD"), new("0", "USD"), new("100", "USD")) { ProductId = "product", SkuSnapshot = "SKU-OLD", ProductTypeSnapshot = "SERVICE" };
        var quote = new Quote("workspace", "quote-source", "Q-0000000001", "CONTACT", "buyer", "DIRECT_SALE", null, null, null,
            new("Source", "USD", "owner", null, JsonSerializer.Serialize(new[] { line }, QuotePricing.Json), "[]", "[{\"index\":0,\"productId\":\"product\",\"snapshot\":{\"unitPrice\":\"100\"}}]", 100, 0, 0, 100,
                new DateOnly(2026, 10, 25), null, null), Now);
        quote.RecordDelivery("[]", Now); return quote;
    }
    internal static void Set(Quote q, string field, object value) => typeof(Quote).GetProperty(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(q, value);
}
