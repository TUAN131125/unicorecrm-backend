using UnicoreCRM.Crm.Deals.Application.Common;
using UnicoreCRM.Crm.Deals.Application.CreateDeal;
using UnicoreCRM.Crm.Deals.Contracts;
using UnicoreCRM.Crm.Deals.Domain;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Deals.Application.CreateLeadOpportunity;

/// <summary>
/// Deals' narrow WF-10 participant. Validation can be run before Contacts commits; creation then
/// delegates to the normal Deal command so no Deal rule is copied into Workflows or Leads.
/// </summary>
internal sealed class Handler(
    CreateDeal.Handler createDeal,
    DealAuthorization authorization,
    DealPipelineRegistry registry,
    IWorkspaceMemberReferenceValidator memberValidator,
    DealReferenceValidation references) : ILeadQualificationDealParticipant
{
    public async Task<LeadOpportunityDealResult> ValidateOpportunityAsync(
        LeadOpportunityDealCommand command,
        CancellationToken cancellationToken)
    {
        var access = await authorization.AuthorizeAsync(
            DealCapabilities.Create,
            new DealRequestMetadata(command.RequestId, command.CorrelationId),
            cancellationToken);
        if (!access.IsSuccess)
            return Failed(access.Error!);

        var request = Request(command, command.ContactId ?? "contact_pending");
        CreateDealValidation.TryCreate(
            request,
            out var profile,
            out var stageCode,
            out var forecastCategory,
            out var nextActionAt,
            out var nextActionSummary,
            out var nextActionTaskId,
            out var fields);
        if (fields.Count != 0)
            return Failed(DealErrors.Validation(fields));
        var configured = await registry.ReadInitialStageForCreateAsync(access.Value!.Trusted, cancellationToken);
        if (configured.Error is not null) return Failed(configured.Error);
        var stage = configured.Stage!;
        forecastCategory = DealStages.DeriveForecast(stage, profile!.OpportunityScore);
        var progressive = DealValidation.ProgressiveProfileErrors(profile!, stage, forecastCategory);
        if (progressive.Count != 0)
            return Failed(DealErrors.ProgressiveProfile(progressive));
        var fieldError = DealFieldSecurity.GuardCreateWrite(
            access.Value!.Authorization, profile!, nextActionAt, nextActionSummary, nextActionTaskId);
        if (fieldError is not null)
            return Failed(fieldError);
        if (!await memberValidator.IsActiveMemberAsync(
                access.Value.Trusted.WorkspaceId, profile!.OwnerId, cancellationToken))
            return Failed(DealErrors.OwnerNotAssignable());
        // Contact NEW does not exist during this preflight. Verify product references through
        // the same owner reader as normal creation before any workflow participant commits.
        // Creation repeats mutable reference checks at its authoritative write boundary.
        var referenceError = await references.ValidateAsync(null, command.InterestedProductIds,
            new DealRequestMetadata(command.RequestId, command.CorrelationId), cancellationToken);
        if (referenceError is not null)
            return Failed(referenceError);
        return new(true, null, null, null, null, null, null);
    }

    public async Task<LeadOpportunityDealResult> CreateOpportunityAsync(
        LeadOpportunityDealCommand command,
        CancellationToken cancellationToken)
    {
        if (command.ContactId is null)
            return new(false, null, null, null, "LEAD_QUALIFICATION_RELATIONSHIP_INVALID", 422, null);

        var result = await createDeal.HandleAsync(
            new CreateDeal.Command(
                Request(command, command.ContactId),
                new DealCommandMetadata(
                    command.RequestId,
                    command.CorrelationId,
                    command.IdempotencyKey,
                    null,
                    command.LeadId,
                    command.IdempotencyScopeActorId)),
            cancellationToken);
        if (!result.IsSuccess)
            return Failed(result.Error!);
        var response = result.Value!;
        return new(
            true,
            response.Result.Deal.Id,
            response.Result.Deal.ResourceVersion,
            response.Outcome,
            null,
            null,
            null);
    }

    private static CreateDealRequest Request(LeadOpportunityDealCommand command, string contactId) => new(
        Name: command.Name,
        BuyerRef: new DealBuyerReference("CONTACT", contactId),
        StageCode: null,
        Amount: command.EstimatedValue,
        OpportunityScore: "10",
        OwnerId: command.OwnerId,
        ExpectedCloseDate: command.ExpectedCloseDate,
        InterestedProductIds: command.InterestedProductIds,
        LineItems: [],
        ContactId: contactId,
        SourceLeadId: command.LeadId,
        NextActionAt: command.NextActionAt,
        NextActionSummary: command.NextActionSummary,
        NextActionTaskId: command.NextActionTaskId,
        Notes: command.NeedSummary);

    private static LeadOpportunityDealResult Failed(DealOperationError error) =>
        new(false, null, null, null, error.Code, error.Status, error.FieldErrors);
}
