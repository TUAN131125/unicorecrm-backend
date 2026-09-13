using UnicoreCRM.BuildingBlocks;

namespace UnicoreCRM.Workflows.Atomic.Domain;

internal sealed class WorkflowIntegrationOutboxMessage
{
    private WorkflowIntegrationOutboxMessage() { }
    internal WorkflowIntegrationOutboxMessage(LeadCustomerConversionAnchor anchor, DateTimeOffset occurredAt)
    {
        EventId=$"workflow_event_{Guid.NewGuid():N}"; WorkspaceId=anchor.WorkspaceId; CorrelationId=anchor.CorrelationId;
        OccurredAt=occurredAt; ExportState="PENDING";
        var data=new { conversionId=anchor.ConversionId, leadId=anchor.LeadId, customerId=anchor.CustomerId!, subjectType=anchor.SubjectType,
            subjectId=anchor.SubjectId, customerResolution=anchor.CustomerResolution!, leadVersion=anchor.LeadVersion!.Value, customerVersion=anchor.CustomerVersion!.Value };
        IntegrationEnvelopeJson=IntegrationEventSerialization.CreateEnvelope(EventId,WorkflowIntegrationEvents.LeadCustomerConverted,
            anchor.WorkspaceId,"Workflows","LEAD_CUSTOMER_CONVERSION",anchor.ConversionId,anchor.LeadVersion,occurredAt,anchor.CorrelationId,data);
    }
    internal string EventId{get;private set;}=null!; internal string WorkspaceId{get;private set;}=null!; internal string CorrelationId{get;private set;}=null!;
    internal DateTimeOffset OccurredAt{get;private set;} internal string IntegrationEnvelopeJson{get;private set;}=null!; internal string? ExportState{get;private set;}
    internal int ExportAttemptCount{get;private set;} internal string? RelayAttemptId{get;private set;} internal DateTimeOffset? LeaseExpiresAt{get;private set;}
    internal DateTimeOffset? NextEligibleAt{get;private set;} internal DateTimeOffset? PublishedAt{get;private set;} internal string? LastRelayError{get;private set;}
    internal void Lease(string id,DateTimeOffset until){ExportState="LEASED";RelayAttemptId=id;LeaseExpiresAt=until;ExportAttemptCount++;}
    internal void Publish(string id,DateTimeOffset now){if(RelayAttemptId!=id||ExportState!="LEASED")throw new InvalidOperationException("Stale relay attempt.");ExportState="PUBLISHED";PublishedAt=now;RelayAttemptId=null;LeaseExpiresAt=null;LastRelayError=null;}
    internal void Release(string id,string error,DateTimeOffset next){if(RelayAttemptId!=id||ExportState!="LEASED")return;ExportState="PENDING";RelayAttemptId=null;LeaseExpiresAt=null;NextEligibleAt=next;LastRelayError=error[..Math.Min(512,error.Length)];}
}
