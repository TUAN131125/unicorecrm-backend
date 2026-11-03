using System.Reflection;
using System.Text.Json;
using UnicoreCRM.Crm.Deals.Application.Common;
using UnicoreCRM.Crm.Deals.Contracts;
using UnicoreCRM.Crm.Deals.Domain;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;
using Xunit;
using ListHandler = UnicoreCRM.Crm.Deals.Application.ListDeals.Handler;
using ListQuery = UnicoreCRM.Crm.Deals.Application.ListDeals.Query;

namespace UnicoreCRM.ArchitectureTests;

public sealed class DealListSecurityTests
{
    private static readonly TrustedWorkspaceContext Trusted = new("workspace-test", "account-test", "member-test", "membership-test");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] OptionalFields = ["notes", "contactId", "sourceLeadId", "forecastCategory", "stageEnteredAt"];

    [Fact]
    public async Task List_withholds_restricted_optional_fields_without_changing_stored_record()
    {
        var (handler, persistence) = Create(RecordFieldEnforcement.Withheld);
        var original = JsonSerializer.Serialize(DealProjection.Document(persistence.Deal), Json);
        using var originalJson = JsonDocument.Parse(original);
        foreach (var field in OptionalFields) Assert.True(originalJson.RootElement.TryGetProperty(field, out _), field);

        var result = await handler.HandleAsync(Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        using var wire = JsonDocument.Parse(JsonSerializer.Serialize(item, Json));
        foreach (var field in OptionalFields) Assert.False(wire.RootElement.TryGetProperty(field, out _), field);
        Assert.Equal(persistence.Deal.DealId, item.Id);
        Assert.Equal(persistence.Deal.Profile.Name, item.Name);
        Assert.Equal(1, result.Value.PageInfo.TotalCount);
        Assert.Equal(original, JsonSerializer.Serialize(DealProjection.Document(persistence.Deal), Json));
        Assert.Equal(1, persistence.Audits);
        Assert.Equal(1, persistence.Saves);
    }

    [Theory]
    [InlineData(RecordFieldEnforcement.ReadOnly)]
    [InlineData(RecordFieldEnforcement.ReadWrite)]
    public async Task List_preserves_readable_fields(RecordFieldEnforcement enforcement)
    {
        var (handler, persistence) = Create(enforcement);
        var result = await handler.HandleAsync(Query(), CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(JsonSerializer.Serialize(DealProjection.Document(persistence.Deal), Json),
            JsonSerializer.Serialize(Assert.Single(result.Value!.Items), Json));
    }

    [Fact]
    public async Task Required_field_restriction_refuses_list_before_persistence_read()
    {
        var (handler, persistence) = Create(RecordFieldEnforcement.Withheld, ["amount"]);
        var result = await handler.HandleAsync(Query(), CancellationToken.None);
        Assert.Equal("ACCESS_DENIED", result.Error?.Code);
        Assert.Null(result.Value);
        Assert.Equal(0, persistence.Reads);
        Assert.Equal(0, persistence.Audits);
        Assert.Equal(0, persistence.Saves);
    }

    private static ListQuery Query() => new(null, 50, null, null, null, null, null, null, null, null, "request-test", "correlation-test");

    [Theory]
    [InlineData("Confidential")]
    [InlineData("contact-secret")]
    [InlineData("lead-secret")]
    public async Task Search_does_not_reveal_withheld_optional_fields_through_counts(string search)
    {
        var (restricted, _) = Create(RecordFieldEnforcement.Withheld);
        var hidden = await restricted.HandleAsync(Query() with { Search = search }, CancellationToken.None);
        Assert.True(hidden.IsSuccess);
        Assert.Empty(hidden.Value!.Items);
        Assert.Equal(0, hidden.Value.PageInfo.TotalCount);

        var (readable, _) = Create(RecordFieldEnforcement.ReadOnly);
        var visible = await readable.HandleAsync(Query() with { Search = search }, CancellationToken.None);
        Assert.True(visible.IsSuccess);
        Assert.Single(visible.Value!.Items);
        Assert.Equal(1, visible.Value.PageInfo.TotalCount);
    }

    [Fact]
    public async Task Search_keeps_visible_name_available_when_optional_fields_are_withheld()
    {
        var (handler, _) = Create(RecordFieldEnforcement.Withheld);
        var result = await handler.HandleAsync(Query() with { Search = "Visible" }, CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
    }

    private static (ListHandler Handler, PersistenceSpy Persistence) Create(
        RecordFieldEnforcement enforcement, IReadOnlyList<string>? unenforceable = null)
    {
        var port = DispatchProxy.Create<IDealsPersistence, PersistenceSpy>();
        var spy = (PersistenceSpy)(object)port;
        spy.Deal = new Deal(Trusted.WorkspaceId,
            new DealProfile("Visible deal", new("CONTACT", "buyer-test"), new("100", "VND"),
                "20", Trusted.MemberId, new DateOnly(2027, 1, 1), "contact-secret", "lead-secret", [], "Confidential notes"),
            "DISCOVERY", DealStageCategory.Open, DealForecastCategory.Pipeline, DateTimeOffset.UnixEpoch);
        return (new(new(new AccessEvaluator(enforcement, unenforceable ?? [])), port, TimeProvider.System), spy);
    }

    public class PersistenceSpy : DispatchProxy
    {
        internal Deal Deal = null!;
        internal int Reads, Audits, Saves;
        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            switch (method?.Name)
            {
                case "ReadDealsAsync":
                    Assert.Equal(Trusted.WorkspaceId, arguments![0]);
                    Reads++;
                    return Task.FromResult<IReadOnlyList<Deal>>([Deal]);
                case "AddAudit": Audits++; return null;
                case "SaveChangesAsync": Saves++; return Task.CompletedTask;
                default: throw new InvalidOperationException($"Unexpected list persistence call: {method?.Name}.");
            }
        }
    }

    private sealed class AccessEvaluator(RecordFieldEnforcement enforcement, IReadOnlyList<string> unenforceable) : IRecordAccessEvaluator
    {
        public Task<RecordAccessAuthorization> AuthorizeResourceAsync(string resourceKey, string capability,
            IReadOnlyList<string>? fields, RecordAccessRepresentation representation, RecordAccessRequestContext context, CancellationToken ct)
        {
            Assert.Equal("deals", resourceKey);
            Assert.Equal("deals.read", capability);
            var policy = (fields ?? []).ToDictionary(field => field,
                field => OptionalFields.Contains(field, StringComparer.Ordinal) ? enforcement : RecordFieldEnforcement.ReadWrite,
                StringComparer.OrdinalIgnoreCase);
            return Task.FromResult(new RecordAccessAuthorization(true, "AUTHORIZED", Trusted,
                RecordAccessScopeFilter.Workspace, null, "WORKSPACE", policy, unenforceable, [capability],
                "test", resourceKey, capability, true));
        }

        public Task<RecordAccessRecordDecision> AuthorizeRecordAsync(RecordAccessAuthorization access, string id,
            RecordAccessFacts facts, string point, RecordAccessRequestContext context, CancellationToken ct) =>
            throw new InvalidOperationException("List record scope belongs in the persistence query.");
    }
}
