using UnicoreCRM.Crm.Deals.Application.Common;
using UnicoreCRM.Crm.Deals.Contracts;
using UnicoreCRM.Crm.Deals.Domain;
using Xunit;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UnicoreCRM.ArchitectureTests;

public sealed class DealPipelinePolicyTests
{
    [Fact]
    public void Legacy_single_and_batch_replays_add_pipeline_without_rewriting_immutable_evidence()
    {
        var now = DateTimeOffset.UtcNow;
        var profile = new DealProfile("Legacy opportunity", new DealBuyer("CONTACT", "contact_a"),
            new DealMoneyValue("100", "VND"), "0", "member_a", new DateOnly(2026, 10, 23), null, null, [], null);
        var deal = new Deal("ws_a", profile, "DISCOVERY", DealStageCategory.Open, DealForecastCategory.Pipeline, now);
        var document = DealProjection.Document(deal);
        var single = new DealMutationResponse("command_a", "correlation_a", deal.DealId, "DEAL", 0,
            DealProjection.Utc(now), "COMMITTED", new(document), [], [], []);
        var json = JsonSerializer.SerializeToNode(single, DealCommandSupport.SerializationOptions)!;
        json["result"]!["deal"]!.AsObject().Remove("pipelineId");
        var evidence = json.ToJsonString();
        var record = new DealIdempotencyRecord("scope", "ws_a", "createDealCommand", "member_a", "WORKSPACE", "key_a", "fingerprint", evidence, now);
        Assert.Equal(DealPipeline.DefaultId("ws_a"), DealCommandSupport.Replay(record).Result.Deal.PipelineId);
        Assert.Equal(evidence, record.ResponseJson);
        var batch = new DealBatchMutationResponse("command_b", "correlation_a", "batch_a", "DEAL", 1,
            DealProjection.Utc(now), "COMMITTED", new([document]), [], [], []);
        var batchJson = JsonSerializer.SerializeToNode(batch, DealCommandSupport.SerializationOptions)!;
        batchJson["result"]!["deals"]![0]!.AsObject().Remove("pipelineId");
        var batchRecord = new DealIdempotencyRecord("scope_b", "ws_a", "archiveDealsBatch", "member_a", "WORKSPACE", "key_b", "fingerprint", batchJson.ToJsonString(), now);
        Assert.All(DealCommandSupport.ReplayBatch(batchRecord).Result.Deals,
            item => Assert.Equal(DealPipeline.DefaultId("ws_a"), item.PipelineId));
    }
    [Fact]
    public void Defaults_are_workspace_stable_and_keep_terminal_invariants()
    {
        var value = DealPipeline.Default("ws_a", "member_a", DateTimeOffset.UtcNow);
        Assert.Equal(DealPipeline.DefaultId("ws_a"), value.PipelineId);
        Assert.NotEqual(value.PipelineId, DealPipeline.DefaultId("ws_b"));
        Assert.Empty(DealPipelineRegistry.Validate(Input(value), value));
        Assert.Equal(5, value.Stages.Count(stage => stage.Category == DealStageCategory.Open));
    }
    [Fact]
    public void Display_changes_cannot_convert_open_stage_or_replace_terminal_identity()
    {
        var value = DealPipeline.Default("ws_a", "member_a", DateTimeOffset.UtcNow);
        var request = Input(value);
        Assert.NotEmpty(DealPipelineRegistry.Validate(request with { Stages = request.Stages!.Select(stage =>
            stage.Code == "DISCOVERY" ? stage with { Category = "WON" } : stage).ToArray() }, value));
        Assert.NotEmpty(DealPipelineRegistry.Validate(request with { Stages = request.Stages!.Where(stage => stage.Code != "LOST").ToArray() }, value));
    }
    [Fact]
    public void Duplicate_codes_orders_and_invalid_probabilities_are_rejected_in_inactive_stages_too()
    {
        var value = DealPipeline.Default("ws_a", "member_a", DateTimeOffset.UtcNow);
        var request = Input(value);
        var stages = request.Stages!.ToArray();
        stages[0] = stages[0] with { Code = "QUALIFIED", IsActive = false, DefaultProbability = 101, Order = stages[1].Order };
        var errors = DealPipelineRegistry.Validate(request with { Stages = stages }, value);
        Assert.True(errors.Count >= 3);
    }
    [Fact]
    public void Custom_stage_uses_score_without_code_or_order_heuristics()
    {
        var stage = new DealPipelineStage("stage_a", "NEGOTIATION_CUSTOM", "Tư vấn", "Advisory", 999,
            DealStageCategory.Open, 99, "teal", true, false);
        Assert.Equal(DealForecastCategory.Pipeline, DealStages.DeriveForecast(stage, "0"));
        Assert.Equal(DealForecastCategory.BestCase, DealStages.DeriveForecast(stage, "50"));
        Assert.Equal(DealForecastCategory.Commit, DealStages.DeriveForecast(stage, "80"));
    }
    [Fact]
    public void Legacy_policies_are_persisted_metadata_and_do_not_follow_labels()
    {
        var pipeline = DealPipeline.Default("ws_a", "member_a", DateTimeOffset.UtcNow);
        var proposal = pipeline.Stages.Single(stage => stage.Code == "PROPOSAL");
        proposal.Configure("Khác", "Different", 0, 0, "slate", true);
        Assert.True(proposal.RequiresPositiveAmount);
        Assert.Equal(DealForecastCategory.BestCase, DealStages.DeriveForecast(proposal, "0"));
        Assert.Equal(DealForecastCategory.Commit, DealStages.DeriveForecast(pipeline.Stages.Single(stage => stage.Code == "NEGOTIATION"), "0"));
    }
    private static SaveDealPipelineRequest Input(DealPipeline value) => new(value.NameVi, value.NameEn, value.Description,
        value.IsActive, value.IsDefault, value.Stages.Select(stage => new DealPipelineStageInput(stage.StageId, stage.Code,
            stage.LabelVi, stage.LabelEn, stage.Order, stage.Category.ToString().ToUpperInvariant(), stage.DefaultProbability, stage.Color, stage.IsActive)).ToArray());
}
