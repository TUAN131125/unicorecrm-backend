namespace UnicoreCRM.Sales.Products.Domain;

internal sealed record ProductMoneyValue(string Amount, string Currency);

internal sealed record ProductProfile(
    string Sku,
    string NormalizedSku,
    string Name,
    string Type,
    string Status,
    string Category,
    string? Description,
    string Unit,
    ProductMoneyValue UnitPrice,
    ProductMoneyValue? CostPrice,
    string TaxRate,
    string TaxMode,
    string BillingCycle,
    bool IsSubscription,
    bool IsRenewable,
    int? WarrantyMonths,
    int? DefaultContractMonths,
    IReadOnlyList<string> Tags);

internal sealed class Product
{
    private Product() { }

    internal Product(string workspaceId, ProductProfile profile, DateTimeOffset now)
    {
        ProductId = ProductIds.New("product");
        WorkspaceId = workspaceId;
        Profile = profile;
        NormalizedSku = profile.NormalizedSku;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public string ProductId { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public ProductProfile Profile { get; private set; } = null!;
    public string NormalizedSku { get; private set; } = null!;
    public DateTimeOffset? ArchivedAt { get; private set; }
    public string? ArchiveReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public long Version { get; private set; }

    internal bool IsArchived => ArchivedAt is not null;

    internal bool Replace(ProductProfile profile, DateTimeOffset now)
    {
        if (IsArchived)
            return false;

        Profile = profile;
        NormalizedSku = profile.NormalizedSku;
        Touch(now);
        return true;
    }

    internal bool Archive(string reason, DateTimeOffset now)
    {
        if (IsArchived)
            return false;

        Profile = Profile with { Status = "ARCHIVED" };
        ArchivedAt = now;
        ArchiveReason = reason;
        Touch(now);
        return true;
    }

    internal bool Restore(DateTimeOffset now)
    {
        if (!IsArchived)
            return false;

        Profile = Profile with { Status = "ACTIVE" };
        ArchivedAt = null;
        ArchiveReason = null;
        Touch(now);
        return true;
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}
