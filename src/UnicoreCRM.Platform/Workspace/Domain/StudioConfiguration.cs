namespace UnicoreCRM.Platform.Workspace.Domain;

internal sealed class StudioConfiguration
{
    private StudioConfiguration() { }

    internal StudioConfiguration(
        string workspaceId,
        string businessInformationJson,
        string addressesJson,
        string localeRegionJson,
        string blueprintJson,
        string featuresJson,
        DateTimeOffset now)
    {
        WorkspaceId = workspaceId;
        Revision = 1;
        PublicationStatus = "DRAFT";
        BusinessInformationJson = businessInformationJson;
        AddressesJson = addressesJson;
        LocaleRegionJson = localeRegionJson;
        BlueprintJson = blueprintJson;
        FeaturesJson = featuresJson;
        UpdatedAt = now;
    }

    public string WorkspaceId { get; private set; } = null!;
    public long Revision { get; private set; }
    public string PublicationStatus { get; private set; } = null!;
    public long? PublishedRevision { get; private set; }
    public string BusinessInformationJson { get; private set; } = null!;
    public string AddressesJson { get; private set; } = null!;
    public string LocaleRegionJson { get; private set; } = null!;
    public string BlueprintJson { get; private set; } = null!;
    public string FeaturesJson { get; private set; } = null!;
    public DateTimeOffset UpdatedAt { get; private set; }
    public string? UpdatedByMemberId { get; private set; }

    internal void UpdateBusinessInformation(string businessInformationJson, string addressesJson, string memberId, DateTimeOffset now)
    {
        BusinessInformationJson = businessInformationJson;
        AddressesJson = addressesJson;
        Touch(memberId, now);
    }

    internal void UpdateLocaleRegion(string localeRegionJson, string memberId, DateTimeOffset now)
    {
        LocaleRegionJson = localeRegionJson;
        Touch(memberId, now);
    }

    internal void UpdateBlueprint(string blueprintJson, string featuresJson, string memberId, DateTimeOffset now)
    {
        BlueprintJson = blueprintJson;
        FeaturesJson = featuresJson;
        Touch(memberId, now);
    }

    internal void UpdateFeatures(string featuresJson, string memberId, DateTimeOffset now)
    {
        FeaturesJson = featuresJson;
        Touch(memberId, now);
    }

    private void Touch(string memberId, DateTimeOffset now)
    {
        Revision++;
        UpdatedAt = now;
        UpdatedByMemberId = memberId;
    }
}

internal sealed class StudioQuickSetup
{
    private static readonly string[] Steps = ["business-profile", "locale-currency", "workspace-blueprint"];
    private StudioQuickSetup() { }

    internal StudioQuickSetup(string workspaceId)
    {
        WorkspaceId = workspaceId;
        Status = "NOT_STARTED";
        CurrentStepId = Steps[0];
        CompletedStepIdsJson = "[]";
        SkippedStepIdsJson = "[]";
        Revision = 1;
        FlowVersion = 3;
    }

    public string WorkspaceId { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public string? CurrentStepId { get; private set; }
    public string CompletedStepIdsJson { get; private set; } = null!;
    public string SkippedStepIdsJson { get; private set; } = null!;
    public DateTimeOffset? AutoOpenDismissedAt { get; private set; }
    public DateTimeOffset? LastOpenedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public long Revision { get; private set; }
    public int FlowVersion { get; private set; }

    internal void Open(DateTimeOffset now)
    {
        LastOpenedAt = now;
        if (Status == "NOT_STARTED") Status = "IN_PROGRESS";
        Revision++;
    }

    internal void Dismiss(DateTimeOffset now)
    {
        AutoOpenDismissedAt = now;
        Revision++;
    }

    internal void ResolveStep(string stepId, bool completed, DateTimeOffset now)
    {
        var index = Array.IndexOf(Steps, stepId);
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(stepId));

        var completedSteps = System.Text.Json.JsonSerializer.Deserialize<List<string>>(CompletedStepIdsJson) ?? [];
        var skippedSteps = System.Text.Json.JsonSerializer.Deserialize<List<string>>(SkippedStepIdsJson) ?? [];
        completedSteps.Remove(stepId);
        skippedSteps.Remove(stepId);
        (completed ? completedSteps : skippedSteps).Add(stepId);
        CompletedStepIdsJson = System.Text.Json.JsonSerializer.Serialize(completedSteps);
        SkippedStepIdsJson = System.Text.Json.JsonSerializer.Serialize(skippedSteps);

        CurrentStepId = Steps
            .FirstOrDefault(step => !completedSteps.Contains(step, StringComparer.Ordinal)
                                    && !skippedSteps.Contains(step, StringComparer.Ordinal));
        if (CurrentStepId is null)
        {
            Status = "COMPLETED";
            CompletedAt = now;
        }
        else
        {
            Status = "IN_PROGRESS";
        }
        Revision++;
    }
}

internal sealed class StudioCommandRecord
{
    private StudioCommandRecord() { }
    internal StudioCommandRecord(string scopeKey, string workspaceId, string operationId, string idempotencyKey, string fingerprint, string responseJson, DateTimeOffset occurredAt)
    {
        ScopeKey = scopeKey;
        WorkspaceId = workspaceId;
        OperationId = operationId;
        IdempotencyKey = idempotencyKey;
        RequestFingerprint = fingerprint;
        ResponseJson = responseJson;
        OccurredAt = occurredAt;
    }
    public string ScopeKey { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public string OperationId { get; private set; } = null!;
    public string IdempotencyKey { get; private set; } = null!;
    public string RequestFingerprint { get; private set; } = null!;
    public string ResponseJson { get; private set; } = null!;
    public DateTimeOffset OccurredAt { get; private set; }
}

internal sealed class StudioConfigurationAudit
{
    private StudioConfigurationAudit() { }
    internal StudioConfigurationAudit(string workspaceId, long revision, string action, string actorMemberId, DateTimeOffset occurredAt, string correlationId, string? summary)
    {
        AuditId = WorkspaceIds.New("audit");
        WorkspaceId = workspaceId;
        Revision = revision;
        Action = action;
        ActorMemberId = actorMemberId;
        OccurredAt = occurredAt;
        CorrelationId = correlationId;
        Summary = summary;
    }
    public string AuditId { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public long Revision { get; private set; }
    public string Action { get; private set; } = null!;
    public string ActorMemberId { get; private set; } = null!;
    public DateTimeOffset OccurredAt { get; private set; }
    public string CorrelationId { get; private set; } = null!;
    public string? Summary { get; private set; }
}

internal sealed class StudioOutboxEvent
{
    private StudioOutboxEvent() { }
    internal StudioOutboxEvent(string workspaceId, string eventType, string aggregateId, long aggregateVersion, string correlationId, string payloadJson, DateTimeOffset occurredAt)
    {
        EventId = WorkspaceIds.New("evt");
        WorkspaceId = workspaceId;
        EventType = eventType;
        AggregateId = aggregateId;
        AggregateVersion = aggregateVersion;
        CorrelationId = correlationId;
        PayloadJson = payloadJson;
        OccurredAt = occurredAt;
    }
    public string EventId { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public string EventType { get; private set; } = null!;
    public string AggregateId { get; private set; } = null!;
    public long AggregateVersion { get; private set; }
    public string CorrelationId { get; private set; } = null!;
    public string PayloadJson { get; private set; } = null!;
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
}
