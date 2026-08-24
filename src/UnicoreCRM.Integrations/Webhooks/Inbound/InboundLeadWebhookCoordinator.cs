using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using UnicoreCRM.Crm.Leads.Contracts;
using UnicoreCRM.Integrations.Application;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.PlatformOperations.Inbox.Contracts;

namespace UnicoreCRM.Integrations.Webhooks.Inbound;

internal sealed partial class InboundLeadWebhookCoordinator(
    IInboundIntegrationBindingStore bindingStore,
    IWebhookSecretProvider secretProvider,
    GenericSignedJsonVerifier verifier,
    IInboundDeliveryInbox inbox,
    ITrustedWorkspaceMemberResolver memberResolver,
    IInboundLeadIngress leadIngress,
    TimeProvider timeProvider,
    ILogger<InboundLeadWebhookCoordinator> logger)
{
    private const string ExtensionAuthority = "PROJECT_EXTENSION_INBOUND_LEAD_WEBHOOK";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal async Task<InboundWebhookExecutionResult> ExecuteAsync(
        VerifiedWebhookRequest request,
        CancellationToken cancellationToken)
    {
        if (!EntityIdPattern().IsMatch(request.IntegrationId)
            || !DeliveryIdPattern().IsMatch(request.DeliveryId))
        {
            return Failure(400, "WEBHOOK_REQUEST_INVALID", "Webhook request is invalid", false, request.CorrelationId);
        }

        var binding = await bindingStore.FindAsync(request.IntegrationId, cancellationToken);
        if (binding is null || !binding.IsEnabled || binding.ProviderCode != "generic-signed-json")
            return Failure(404, "INTEGRATION_NOT_AVAILABLE", "Integration is not available", false, request.CorrelationId);

        var secret = secretProvider.Resolve(binding.SecretReference);
        if (secret is null || secret.Length < 32)
            return Failure(503, "INTEGRATION_UNAVAILABLE", "Integration is unavailable", true, request.CorrelationId);

        var verification = verifier.Verify(
            request.Timestamp,
            request.DeliveryId,
            request.Signature,
            request.RawPayload,
            secret);
        if (verification == SignatureVerificationResult.MalformedTimestamp)
            return Failure(400, "WEBHOOK_TIMESTAMP_INVALID", "Webhook timestamp is invalid", false, request.CorrelationId);
        if (verification == SignatureVerificationResult.ExpiredTimestamp)
            return Failure(401, "WEBHOOK_TIMESTAMP_EXPIRED", "Webhook timestamp is outside the replay window", false, request.CorrelationId);
        if (verification == SignatureVerificationResult.InvalidSignature)
            return Failure(401, "WEBHOOK_SIGNATURE_INVALID", "Webhook signature is invalid", false, request.CorrelationId);

        var payloadHash = Convert.ToHexString(SHA256.HashData(request.RawPayload));
        var now = timeProvider.GetUtcNow();
        var admission = await inbox.AdmitAsync(
            new InboundDelivery(
                binding.IntegrationId,
                request.DeliveryId,
                payloadHash,
                binding.ProviderCode,
                binding.WorkspaceId,
                binding.DelegatedMemberId,
                request.CorrelationId,
                now),
            cancellationToken);
        if (admission.Kind == InboxAdmissionKind.Conflict)
            return Failure(409, "DELIVERY_ID_CONFLICT", "Delivery identifier conflicts with an earlier payload", false, request.CorrelationId);
        if (admission.Kind == InboxAdmissionKind.Replay)
        {
            if (admission.LeadId is null)
                throw new InvalidOperationException("A processed Inbox delivery must retain its resulting Lead identifier.");
            return InboundWebhookExecutionResult.Success(new InboundLeadWebhookReceipt(
                binding.IntegrationId,
                request.DeliveryId,
                admission.LeadId,
                "REPLAYED",
                request.CorrelationId));
        }

        GenericLeadWebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<GenericLeadWebhookPayload>(request.RawPayload, JsonOptions);
        }
        catch (JsonException)
        {
            await inbox.FailAsync(admission.InboxMessageId, "MALFORMED_PAYLOAD", now, cancellationToken);
            return Failure(400, "MALFORMED_PAYLOAD", "Webhook payload is malformed", false, request.CorrelationId);
        }
        if (payload is null)
        {
            await inbox.FailAsync(admission.InboxMessageId, "MALFORMED_PAYLOAD", now, cancellationToken);
            return Failure(400, "MALFORMED_PAYLOAD", "Webhook payload is malformed", false, request.CorrelationId);
        }

        var trusted = await memberResolver.ResolveActiveMemberAsync(
            binding.WorkspaceId,
            binding.DelegatedMemberId,
            cancellationToken);
        if (trusted is null)
        {
            await inbox.FailAsync(admission.InboxMessageId, "INTEGRATION_AUTHORIZATION_DENIED", now, cancellationToken);
            return Failure(403, "INTEGRATION_AUTHORIZATION_DENIED", "Integration is not authorized", false, request.CorrelationId);
        }

        var createRequest = new CreateLeadRequest
        {
            DisplayName = payload.DisplayName,
            Source = payload.Source,
            OwnerId = binding.DelegatedMemberId,
            EstimatedValue = payload.EstimatedValue is null
                ? null
                : new Money(payload.EstimatedValue.Amount, payload.EstimatedValue.Currency),
            Email = payload.Email,
            Phone = payload.Phone,
            CompanyName = payload.CompanyName,
            Description = payload.Description
        };
        var identityHash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes($"{ExtensionAuthority}\n{binding.IntegrationId}\n{request.DeliveryId}")));
        var leadResult = await leadIngress.CreateAsync(
            new InboundLeadCreateCommand(
                trusted,
                createRequest,
                new LeadExecutionProvenance(
                    "Integration",
                    binding.IntegrationId,
                    binding.DelegatedMemberId,
                    request.DeliveryId),
                $"inbound-lead-webhook_{identityHash[..32]}",
                request.CorrelationId,
                $"inbound-lead-webhook_{identityHash}"),
            cancellationToken);
        if (!leadResult.IsSuccess)
        {
            var publicError = MapLeadFailure(leadResult, request.CorrelationId);
            await inbox.FailAsync(admission.InboxMessageId, publicError.Problem!.Code, timeProvider.GetUtcNow(), cancellationToken);
            return publicError;
        }

        await inbox.CompleteAsync(
            admission.InboxMessageId,
            leadResult.LeadId!,
            leadResult.Outcome == "REPLAYED" ? "LEAD_REPLAYED" : "LEAD_CREATED",
            timeProvider.GetUtcNow(),
            cancellationToken);
        logger.LogInformation(
            "Inbound delivery {DeliveryId} for Integration {IntegrationId} created or replayed Lead {LeadId} in Workspace {WorkspaceId}.",
            request.DeliveryId,
            binding.IntegrationId,
            leadResult.LeadId,
            binding.WorkspaceId);
        return InboundWebhookExecutionResult.Success(new InboundLeadWebhookReceipt(
            binding.IntegrationId,
            request.DeliveryId,
            leadResult.LeadId!,
            "PROCESSED",
            request.CorrelationId));
    }

    private static InboundWebhookExecutionResult MapLeadFailure(
        InboundLeadCreateResult result,
        string correlationId) => result.ErrorCode switch
        {
            "ACCESS_DENIED" or "WORKSPACE_MISMATCH" =>
                Failure(403, "INTEGRATION_AUTHORIZATION_DENIED", "Integration is not authorized", false, correlationId),
            "VALIDATION_FAILED" =>
                Failure(422, "LEAD_VALIDATION_FAILED", "Lead payload validation failed", false, correlationId, result.FieldErrors),
            "IDEMPOTENCY_KEY_REUSED" =>
                Failure(409, "DELIVERY_PROCESSING_CONFLICT", "Delivery processing conflict", false, correlationId),
            _ => Failure(503, "INTEGRATION_PROCESSING_FAILED", "Integration processing failed", true, correlationId)
        };

    private static InboundWebhookExecutionResult Failure(
        int status,
        string code,
        string title,
        bool retryable,
        string correlationId,
        IReadOnlyDictionary<string, string[]>? fieldErrors = null) =>
        InboundWebhookExecutionResult.Failure(status, code, title, retryable, correlationId, fieldErrors);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityIdPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex DeliveryIdPattern();
}
