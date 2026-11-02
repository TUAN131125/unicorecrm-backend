using System.Security.Cryptography;
using System.Text;

namespace UnicoreCRM.Crm.Deals.Domain;

internal sealed class DealPipeline
{
    private DealPipeline() { }
    internal DealPipeline(string workspaceId, string pipelineId, string nameVi, string nameEn,
        string description, bool isDefault, bool isActive, IEnumerable<DealPipelineStage> stages,
        string actor, DateTimeOffset now)
    {
        WorkspaceId = workspaceId; PipelineId = pipelineId; NameVi = nameVi; NameEn = nameEn;
        Description = description; IsDefault = isDefault; IsActive = isActive;
        Stages = stages.ToList(); CreatedAt = now; UpdatedAt = now; UpdatedBy = actor; Version = 1;
    }
    public string WorkspaceId { get; private set; } = null!;
    public string PipelineId { get; private set; } = null!;
    public string NameVi { get; private set; } = null!;
    public string NameEn { get; private set; } = null!;
    public string Description { get; private set; } = "";
    public bool IsDefault { get; private set; }
    public bool IsActive { get; private set; }
    public long Version { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = null!;
    public List<DealPipelineStage> Stages { get; private set; } = [];

    internal void Replace(string nameVi, string nameEn, string description, bool active, string actor, DateTimeOffset now)
    {
        NameVi = nameVi; NameEn = nameEn; Description = description; IsActive = active; Touch(actor, now);
    }
    internal void SetDefault(bool value, string actor, DateTimeOffset now)
    { IsDefault = value; Touch(actor, now); }
    private void Touch(string actor, DateTimeOffset now) { Version++; UpdatedAt = now; UpdatedBy = actor; }

    internal static string DefaultId(string workspaceId) => "dpl_" +
        Convert.ToHexString(SHA256.HashData(Encoding.Unicode.GetBytes(workspaceId)))[..32].ToLowerInvariant();

    internal static DealPipeline Default(string workspaceId, string actor, DateTimeOffset now) => new(
        workspaceId, DefaultId(workspaceId), "Quy trình bán hàng", "Sales pipeline", "", true, true,
        [
            Stage("DISCOVERY", "Khám phá", "Discovery", 10, 10, "blue"),
            Stage("QUALIFIED", "Đã đánh giá", "Qualified", 20, 30, "teal"),
            Stage("SOLUTION", "Giải pháp", "Solution", 30, 50, "purple", floor: DealForecastCategory.BestCase),
            Stage("PROPOSAL", "Đề xuất", "Proposal", 40, 65, "yellow", floor: DealForecastCategory.BestCase, positive: true),
            Stage("NEGOTIATION", "Đàm phán", "Negotiation", 50, 80, "orange", floor: DealForecastCategory.Commit, positive: true),
            Stage("WON", "Thắng", "Won", 60, 100, "green", DealStageCategory.Won),
            Stage("LOST", "Thất bại", "Lost", 70, 0, "red", DealStageCategory.Lost)
        ], actor, now);

    private static DealPipelineStage Stage(string code, string vi, string en, int order, int probability,
        string color, DealStageCategory category = DealStageCategory.Open,
        DealForecastCategory floor = DealForecastCategory.Pipeline, bool positive = false) =>
        new("dst_" + code, code, vi, en, order, category, probability, color, true, true, floor, positive);
}

internal sealed class DealPipelineStage
{
    private DealPipelineStage() { }
    internal DealPipelineStage(string stageId, string code, string labelVi, string labelEn, int order,
        DealStageCategory category, int defaultProbability, string color, bool active, bool system,
        DealForecastCategory forecastFloor = DealForecastCategory.Pipeline, bool requiresPositiveAmount = false)
    {
        StageId = stageId; Code = code; LabelVi = labelVi; LabelEn = labelEn; Order = order;
        Category = category; DefaultProbability = defaultProbability; Color = color;
        IsActive = active; IsSystem = system; ForecastFloor = forecastFloor; RequiresPositiveAmount = requiresPositiveAmount;
    }
    public string StageId { get; private set; } = null!;
    public string Code { get; private set; } = null!;
    public string LabelVi { get; private set; } = null!;
    public string LabelEn { get; private set; } = null!;
    public int Order { get; private set; }
    public DealStageCategory Category { get; private set; }
    public int DefaultProbability { get; private set; }
    public string Color { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public bool IsSystem { get; private set; }
    public DealForecastCategory ForecastFloor { get; private set; }
    public bool RequiresPositiveAmount { get; private set; }
    internal void Configure(string vi, string en, int order, int probability, string color, bool active)
    { LabelVi = vi; LabelEn = en; Order = order; DefaultProbability = probability; Color = color; IsActive = active; }
}
