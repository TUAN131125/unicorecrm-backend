using System.Text.Json.Serialization;

namespace UnicoreCRM.Platform.Workspace.Contracts;

public sealed record WorkspaceBusinessInformationDocument(
    string DisplayName,
    string TradingName,
    string LegalName,
    string RegistrationNumber,
    string TaxId,
    string Industry,
    string RepresentativeName,
    string Email,
    string SupportEmail,
    string BillingEmail,
    string Phone,
    string Website,
    string LogoReference);

public sealed record WorkspaceBusinessAddressDocument(
    string Id,
    string Name,
    IReadOnlyList<string> Purposes,
    string AddressLine1,
    string AddressLine2,
    string CountryCode,
    string ProvinceCode,
    string DistrictCode,
    string WardCode,
    string PostalCode,
    string ContactName,
    string ContactPhone,
    bool Active);

public sealed record WorkspaceCurrencyConfigurationDocument(
    string BaseCurrency,
    IReadOnlyList<string> EnabledCurrencies,
    string DisplayMode,
    string ExchangeRateMode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? ExchangeRateProviderConnectionId);

public sealed record WorkspaceExchangeRateDocument(
    string Id,
    string FromCurrency,
    string ToCurrency,
    string Rate,
    DateTimeOffset EffectiveAt,
    string Source,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? ProviderConnectionId,
    string Status,
    long Version);

public sealed record WorkspaceLocaleRegionDocument(
    IReadOnlyList<string> SupportedLocales,
    string DefaultLocale,
    string Timezone,
    string CountryCode,
    string DateFormat,
    int WeekStartsOn,
    WorkspaceCurrencyConfigurationDocument Currencies,
    IReadOnlyList<WorkspaceExchangeRateDocument> ExchangeRates);

public sealed record WorkspaceWorkflowDocument(
    string DealUsageMode,
    string QuoteUsageMode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? QuoteRequirement,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PaymentMode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? OrderMode,
    string DefaultCustomerType,
    string DefaultRevenueModel,
    string SalesMotion,
    string PipelineTemplate);

public sealed record WorkspaceBlueprintDocument(string BusinessModel, WorkspaceWorkflowDocument Workflow);

public sealed record WorkspaceFeatureUsageDocument(
    bool Leads,
    bool Customers,
    bool Contacts,
    bool Deals,
    bool Quotes,
    bool Orders,
    bool Support,
    bool Organizations,
    bool Tasks,
    bool Payments,
    bool Invoices,
    bool Shipping,
    bool Returns);

public sealed record StudioCoreConfigurationDocument(
    string WorkspaceId,
    long Revision,
    string PublicationStatus,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? PublishedRevision,
    WorkspaceBusinessInformationDocument BusinessInformation,
    IReadOnlyList<WorkspaceBusinessAddressDocument> Addresses,
    WorkspaceLocaleRegionDocument LocaleRegion,
    WorkspaceFeatureUsageDocument Features,
    DateTimeOffset UpdatedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? UpdatedByMemberId,
    WorkspaceBlueprintDocument Blueprint);

public sealed record UpdateWorkspaceBusinessInformationRequest(
    WorkspaceBusinessInformationDocument BusinessInformation,
    IReadOnlyList<WorkspaceBusinessAddressDocument> Addresses);

public sealed record UpdateWorkspaceLocaleRegionRequest(WorkspaceLocaleRegionDocument LocaleRegion);
public sealed record UpdateWorkspaceBlueprintRequest(WorkspaceBlueprintDocument Blueprint, WorkspaceFeatureUsageDocument Features);
public sealed record UpdateWorkspaceFeaturesRequest(WorkspaceFeatureUsageDocument Features);
public sealed record PublishWorkspaceConfigurationRequest(string? Note);
public sealed record EmptyCommandRequest;

public sealed record StudioConfigurationMutationResponse(
    string CommandId,
    string CorrelationId,
    string AggregateId,
    string AggregateType,
    long Version,
    DateTimeOffset OccurredAt,
    string Outcome,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> EmittedEventIds,
    IReadOnlyList<string> AuditEvidenceIds,
    StudioCoreConfigurationDocument Result);

public sealed record StudioQuickSetupStateDocument(
    string WorkspaceId,
    string Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? CurrentStepId,
    IReadOnlyList<string> CompletedStepIds,
    IReadOnlyList<string> SkippedStepIds,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DateTimeOffset? AutoOpenDismissedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DateTimeOffset? LastOpenedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DateTimeOffset? CompletedAt,
    long Revision,
    int FlowVersion);

public sealed record StudioQuickSetupMutationResponse(
    string CommandId,
    string CorrelationId,
    string AggregateId,
    string AggregateType,
    long Version,
    DateTimeOffset OccurredAt,
    string Outcome,
    IReadOnlyList<string> AuditEvidenceIds,
    StudioQuickSetupStateDocument Result);

public sealed record StudioConfigurationAuditEntryDocument(
    string AuditId,
    string WorkspaceId,
    long Revision,
    string Action,
    string ActorMemberId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Summary);

public sealed record StudioConfigurationAuditListDocument(
    string WorkspaceId,
    IReadOnlyList<StudioConfigurationAuditEntryDocument> Items,
    DateTimeOffset GeneratedAt);
