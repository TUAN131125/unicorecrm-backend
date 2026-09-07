using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Platform.Workspace.Domain;

namespace UnicoreCRM.Platform.Workspace.Application.Common;

internal enum StudioConfigurationChangeKind { BusinessInformation, LocaleRegion, Blueprint, Features }
internal enum StudioQuickSetupChangeKind { Open, Dismiss, CompleteStep, SkipStep }

internal sealed record StudioConfigurationChange(
    StudioConfigurationChangeKind Kind,
    string OperationId,
    string Action,
    string EventType,
    long ExpectedVersion,
    string IdempotencyKey,
    string Fingerprint,
    string CorrelationId,
    string ActorMemberId,
    string? BusinessInformationJson = null,
    string? AddressesJson = null,
    string? LocaleRegionJson = null,
    string? BlueprintJson = null,
    string? FeaturesJson = null,
    string? Summary = null);

internal sealed record StudioQuickSetupChange(
    StudioQuickSetupChangeKind Kind,
    string OperationId,
    string Action,
    string EventType,
    long ExpectedVersion,
    string IdempotencyKey,
    string Fingerprint,
    string CorrelationId,
    string ActorMemberId,
    string? StepId = null);

internal enum StudioCommitStatus { Committed, Replayed, VersionConflict, IdempotencyKeyReused, NotFound }
internal sealed record StudioConfigurationCommit(StudioCommitStatus Status, StudioConfigurationMutationResponse? Response = null);
internal sealed record StudioQuickSetupCommit(StudioCommitStatus Status, StudioQuickSetupMutationResponse? Response = null);

internal interface IStudioPersistence
{
    Task<StudioConfiguration?> FindConfigurationAsync(string workspaceId, CancellationToken cancellationToken);
    Task<StudioQuickSetup?> FindQuickSetupAsync(string workspaceId, CancellationToken cancellationToken);
    Task<IReadOnlyList<StudioConfigurationAudit>> ListAuditAsync(string workspaceId, CancellationToken cancellationToken);
    Task RecordReadAsync(string operationId, TrustedWorkspaceContext context, WorkspaceRequest request, DateTimeOffset now, CancellationToken cancellationToken);
    Task<StudioConfigurationCommit> CommitConfigurationAsync(string workspaceId, StudioConfigurationChange change, DateTimeOffset now, CancellationToken cancellationToken);
    Task<StudioQuickSetupCommit> CommitQuickSetupAsync(string workspaceId, StudioQuickSetupChange change, DateTimeOffset now, CancellationToken cancellationToken);
}

internal static class StudioJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    internal static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    internal static T Deserialize<T>(string value) => JsonSerializer.Deserialize<T>(value, Options)
        ?? throw new InvalidOperationException($"Stored Studio document {typeof(T).Name} is invalid.");
    internal static string Fingerprint(string operationId, object body) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(operationId + "\n" + Serialize(body))));
}

internal static class StudioDefaults
{
    internal static (StudioConfiguration Configuration, StudioQuickSetup QuickSetup) Create(
        string workspaceId,
        string name,
        string locale,
        string timeZone,
        string baseCurrency,
        IReadOnlyList<string> enabledModuleKeys,
        DateTimeOffset now)
    {
        var countryCode = locale == "vi" ? "VN" : "US";
        var business = new WorkspaceBusinessInformationDocument(name, "", "", "", "", "", "", "", "", "", "", "", "");
        var localeRegion = new WorkspaceLocaleRegionDocument(
            locale == "vi" ? ["vi", "en"] : ["en", "vi"],
            locale,
            timeZone,
            countryCode,
            locale == "vi" ? "DD/MM/YYYY" : "MM/DD/YYYY",
            1,
            new WorkspaceCurrencyConfigurationDocument(baseCurrency, [baseCurrency], "FULL", "MANUAL", null),
            []);
        var blueprint = new WorkspaceBlueprintDocument(
            "B2B",
            new WorkspaceWorkflowDocument("OPTIONAL", "QUOTE", null, null, null, "COMPANY", "SUBSCRIPTION", "ENTERPRISE_SALES", "B2B_SALES"));
        var enabled = enabledModuleKeys.ToHashSet(StringComparer.Ordinal);
        var features = new WorkspaceFeatureUsageDocument(
            enabled.Contains("leads"),
            true,
            enabled.Contains("contacts"),
            enabled.Contains("deals"),
            enabled.Contains("quotes"),
            enabled.Contains("orders"),
            enabled.Contains("support"),
            enabled.Contains("organizations"),
            enabled.Contains("tasks"),
            enabled.Contains("payments"),
            enabled.Contains("invoices"),
            enabled.Contains("shipping"),
            enabled.Contains("returns"));
        return (
            new StudioConfiguration(
                workspaceId,
                StudioJson.Serialize(business),
                "[]",
                StudioJson.Serialize(localeRegion),
                StudioJson.Serialize(blueprint),
                StudioJson.Serialize(features),
                now),
            new StudioQuickSetup(workspaceId));
    }

    internal static StudioCoreConfigurationDocument Project(StudioConfiguration value) => new(
        value.WorkspaceId,
        value.Revision,
        value.PublicationStatus,
        value.PublishedRevision,
        StudioJson.Deserialize<WorkspaceBusinessInformationDocument>(value.BusinessInformationJson),
        StudioJson.Deserialize<WorkspaceBusinessAddressDocument[]>(value.AddressesJson),
        StudioJson.Deserialize<WorkspaceLocaleRegionDocument>(value.LocaleRegionJson),
        StudioJson.Deserialize<WorkspaceFeatureUsageDocument>(value.FeaturesJson),
        value.UpdatedAt,
        value.UpdatedByMemberId,
        StudioJson.Deserialize<WorkspaceBlueprintDocument>(value.BlueprintJson));

    internal static StudioQuickSetupStateDocument Project(StudioQuickSetup value) => new(
        value.WorkspaceId,
        value.Status,
        value.CurrentStepId,
        StudioJson.Deserialize<string[]>(value.CompletedStepIdsJson),
        StudioJson.Deserialize<string[]>(value.SkippedStepIdsJson),
        value.AutoOpenDismissedAt,
        value.LastOpenedAt,
        value.CompletedAt,
        value.Revision,
        value.FlowVersion);
}

internal static class StudioValidation
{
    private static readonly HashSet<string> Locales = ["vi", "en"];
    private static readonly HashSet<string> AddressPurposes = ["REGISTERED", "OPERATING", "INVOICE", "PICKUP", "RETURN"];
    internal static IReadOnlyDictionary<string, string[]> Business(UpdateWorkspaceBusinessInformationRequest request)
    {
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (request.BusinessInformation is null)
        {
            fields["businessInformation"] = ["businessInformation is required."];
            return fields;
        }
        if (request.Addresses is null)
        {
            fields["addresses"] = ["addresses is required."];
            return fields;
        }
        CheckRequired(fields, "businessInformation.displayName", request.BusinessInformation.DisplayName, 200);
        CheckMax(fields, "businessInformation.tradingName", request.BusinessInformation.TradingName, 200);
        CheckMax(fields, "businessInformation.legalName", request.BusinessInformation.LegalName, 240);
        CheckMax(fields, "businessInformation.email", request.BusinessInformation.Email, 320);
        if (request.Addresses.Count > 100) fields["addresses"] = ["addresses must contain at most 100 items."];
        for (var index = 0; index < request.Addresses.Count; index++)
        {
            var address = request.Addresses[index];
            if (address is null || address.CountryCode?.Length != 2 || address.Purposes is null
                || address.Purposes.Count > 5 || address.Purposes.Any(value => !AddressPurposes.Contains(value)))
                fields[$"addresses[{index}]"] = ["address contains invalid required values."];
        }
        return fields;
    }

    internal static IReadOnlyDictionary<string, string[]> Locale(UpdateWorkspaceLocaleRegionRequest request)
    {
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (request.LocaleRegion is null)
        {
            fields["localeRegion"] = ["localeRegion is required."];
            return fields;
        }
        if (request.LocaleRegion.SupportedLocales is null || request.LocaleRegion.Currencies is null || request.LocaleRegion.ExchangeRates is null)
        {
            fields["localeRegion"] = ["localeRegion contains missing required values."];
            return fields;
        }
        if (request.LocaleRegion.SupportedLocales.Count is < 1 or > 10)
            fields["localeRegion.supportedLocales"] = ["supportedLocales must contain between 1 and 10 items."];
        else if (request.LocaleRegion.SupportedLocales.Any(value => !Locales.Contains(value)))
            fields["localeRegion.supportedLocales"] = ["supportedLocales contains an unsupported locale."];
        if (string.IsNullOrWhiteSpace(request.LocaleRegion.DefaultLocale)
            || !request.LocaleRegion.SupportedLocales.Contains(request.LocaleRegion.DefaultLocale, StringComparer.Ordinal))
            fields["localeRegion.defaultLocale"] = ["defaultLocale must be included in supportedLocales."];
        CheckRequired(fields, "localeRegion.timezone", request.LocaleRegion.Timezone, 120);
        if (request.LocaleRegion.CountryCode?.Length != 2)
            fields["localeRegion.countryCode"] = ["countryCode must contain exactly 2 characters."];
        if (request.LocaleRegion.WeekStartsOn is not 0 and not 1)
            fields["localeRegion.weekStartsOn"] = ["weekStartsOn must be 0 or 1."];
        if (request.LocaleRegion.DateFormat is not ("DD/MM/YYYY" or "MM/DD/YYYY" or "YYYY-MM-DD"))
            fields["localeRegion.dateFormat"] = ["dateFormat is unsupported."];
        if (request.LocaleRegion.Currencies.EnabledCurrencies is null
            || request.LocaleRegion.Currencies.EnabledCurrencies.Count == 0
            || string.IsNullOrWhiteSpace(request.LocaleRegion.Currencies.BaseCurrency)
            || !request.LocaleRegion.Currencies.EnabledCurrencies.Contains(request.LocaleRegion.Currencies.BaseCurrency, StringComparer.Ordinal))
            fields["localeRegion.currencies.baseCurrency"] = ["baseCurrency must be included in enabledCurrencies."];
        else if (request.LocaleRegion.Currencies.EnabledCurrencies.Any(value => value is null || value.Length != 3 || value.Any(character => character is < 'A' or > 'Z')))
            fields["localeRegion.currencies.enabledCurrencies"] = ["enabledCurrencies must contain uppercase three-letter currency codes."];
        if (request.LocaleRegion.Currencies.DisplayMode is not ("FULL" or "COMPACT"))
            fields["localeRegion.currencies.displayMode"] = ["displayMode is unsupported."];
        if (request.LocaleRegion.Currencies.ExchangeRateMode is not ("MANUAL" or "CONNECTED_PROVIDER"))
            fields["localeRegion.currencies.exchangeRateMode"] = ["exchangeRateMode is unsupported."];
        if (request.LocaleRegion.ExchangeRates.Count > 500)
            fields["localeRegion.exchangeRates"] = ["exchangeRates must contain at most 500 items."];
        return fields;
    }

    internal static IReadOnlyDictionary<string, string[]> Blueprint(UpdateWorkspaceBlueprintRequest request)
    {
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (request.Blueprint is null || request.Blueprint.Workflow is null)
            fields["blueprint"] = ["blueprint is required."];
        else
        {
            var workflow = request.Blueprint.Workflow;
            if (request.Blueprint.BusinessModel is not ("B2B" or "B2C" or "HYBRID")
                || workflow.DealUsageMode is not ("OPTIONAL" or "DISABLED")
                || workflow.QuoteUsageMode is not ("QUOTE" or "OFFER" or "PROPOSAL" or "DISABLED")
                || workflow.DefaultCustomerType is not ("COMPANY" or "INDIVIDUAL")
                || workflow.DefaultRevenueModel is not ("ONE_TIME" or "SUBSCRIPTION" or "CONTRACT" or "PACKAGE" or "ORDER" or "PROJECT")
                || workflow.SalesMotion is not ("CONSULTATIVE_SALES" or "ENTERPRISE_SALES" or "ORDER_BASED" or "SUBSCRIPTION" or "SERVICE_BOOKING" or "RETAIL" or "PROJECT_BASED")
                || workflow.PipelineTemplate is not ("B2B_SALES" or "B2C_CONSULTATIVE" or "ORDER_BASED" or "SERVICE_BOOKING" or "PROJECT_SALES"))
                fields["blueprint"] = ["blueprint contains an unsupported value."];
        }
        if (request.Features is null)
            fields["features"] = ["features is required."];
        return fields;
    }

    internal static IReadOnlyDictionary<string, string[]> Features(UpdateWorkspaceFeaturesRequest request) =>
        request.Features is null
            ? new Dictionary<string, string[]> { ["features"] = ["features is required."] }
            : new Dictionary<string, string[]>();

    internal static bool IsStep(string? stepId) => stepId is "business-profile" or "locale-currency" or "workspace-blueprint";

    private static void CheckRequired(Dictionary<string, string[]> fields, string name, string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > max)
            fields[name] = [$"{name} must contain between 1 and {max} characters."];
    }

    private static void CheckMax(Dictionary<string, string[]> fields, string name, string? value, int max)
    {
        if (value is null) fields[name] = [$"{name} is required."];
        else if (value.Length > max) fields[name] = [$"{name} must contain at most {max} characters."];
    }
}
