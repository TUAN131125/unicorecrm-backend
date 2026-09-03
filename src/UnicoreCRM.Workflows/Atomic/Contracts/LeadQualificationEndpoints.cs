using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Workflows.Atomic.Contracts;

public static class LeadQualificationEndpoints
{
    public static IEndpointRouteBuilder MapLeadQualificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // The single admitted positive-qualification operation. qualifyLeadForOpportunity and
        // qualifyLeadForDirectSale stay unmapped: neither has an implemented downstream participant.
        // The retired generic qualifyLead remains route-less.
        endpoints.MapPost("/workflows/lead-qualification/{leadId}/nurture", QualifyLeadForNurtureAsync)
            .RequireAuthorization()
            .RequireTrustedWorkspace()
            .WithName("qualifyLeadForNurture");
        return endpoints;
    }

    /// <summary>
    /// A thin transport adapter. It parses headers and the adopted request body, maps the closed
    /// relationship vocabulary, and delegates. No precondition, authorization, idempotency,
    /// concurrency or convergence decision is taken here - all of it belongs to the coordinator and
    /// the owner participants, and duplicating any of it would create a second authority.
    /// </summary>
    private static async Task<IResult> QualifyLeadForNurtureAsync(
        string leadId,
        HttpContext context,
        ILeadNurtureQualificationWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (!LeadQualificationHttp.TryMetadata(context, out var metadata, out var metadataError))
            return metadataError!;

        var body = await LeadQualificationHttp.ReadBodyAsync<QualifyLeadNurtureRequest>(
            context, metadata!.CorrelationId, cancellationToken);
        if (body.Error is not null)
            return body.Error;

        if (!LeadQualificationHttp.TryIntent(body.Value!, out var intent, out var intentError))
            return LeadQualificationHttp.Error(intentError!, metadata.CorrelationId);

        var result = await workflow.ExecuteAsync(
            new LeadNurtureQualificationCommand(
                leadId,
                intent!,
                body.Value!.RevisitAt ?? string.Empty,
                body.Value.Reason ?? string.Empty,
                body.Value.Note,
                metadata.RequestId,
                metadata.CorrelationId,
                metadata.IdempotencyKey,
                metadata.ExpectedVersion,
                body.Value.OwnerId),
            cancellationToken);

        if (!result.IsSuccess)
        {
            return LeadQualificationHttp.Error(
                new LeadQualificationHttp.Failure(
                    result.ErrorCode!,
                    result.ErrorStatus!.Value,
                    result.FieldErrors,
                    result.ExpectedVersion,
                    result.CurrentVersion,
                    result.IdempotencyKey),
                metadata.CorrelationId);
        }

        // Both COMMITTED and REPLAYED are 200 on this operation: the adopted contract declares a
        // single 200 success and carries the distinction in the response body's outcome.
        return Results.Json(result.Response, LeadQualificationHttp.ResponseJson, statusCode: StatusCodes.Status200OK);
    }
}

internal static class LeadQualificationHttp
{
    internal sealed record Failure(
        string Code,
        int Status,
        IReadOnlyDictionary<string, string[]>? FieldErrors = null,
        long? ExpectedVersion = null,
        long? CurrentVersion = null,
        string? IdempotencyKey = null);

    internal sealed record Metadata(
        string RequestId,
        string CorrelationId,
        string IdempotencyKey,
        long ExpectedVersion);

    internal sealed record BodyRead<T>(T? Value, IResult? Error) where T : class;

    internal static readonly JsonSerializerOptions ResponseJson = new(JsonSerializerDefaults.Web);

    private static readonly JsonSerializerOptions StrictJson = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    /// <summary>Every header the adopted operation declares is required.</summary>
    internal static bool TryMetadata(HttpContext context, out Metadata? metadata, out IResult? error)
    {
        metadata = null;
        error = null;
        var requestId = context.Request.Headers["X-Request-Id"].ToString();
        var suppliedCorrelation = context.Request.Headers["X-Correlation-Id"].ToString();
        var correlationId = suppliedCorrelation.Length is >= 8 and <= 128 ? suppliedCorrelation : context.TraceIdentifier;
        var idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString();
        var ifMatch = context.Request.Headers.IfMatch.ToString();

        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (requestId.Length is < 8 or > 128)
            fields["X-Request-Id"] = ["X-Request-Id must contain between 8 and 128 characters."];
        if (suppliedCorrelation.Length != 0 && suppliedCorrelation.Length is < 8 or > 128)
            fields["X-Correlation-Id"] = ["X-Correlation-Id must contain between 8 and 128 characters."];
        if (idempotencyKey.Length is < 8 or > 128)
            fields["Idempotency-Key"] = ["Idempotency-Key must contain between 8 and 128 characters."];
        if (!TryExpectedVersion(ifMatch, out var expectedVersion))
            fields["If-Match"] = ["If-Match must contain a quoted non-negative resource version."];

        if (fields.Count != 0)
        {
            error = Error(new Failure("VALIDATION_FAILED", StatusCodes.Status422UnprocessableEntity, fields), correlationId);
            return false;
        }

        context.Response.Headers["X-Correlation-Id"] = correlationId;
        metadata = new Metadata(requestId, correlationId, idempotencyKey, expectedVersion!.Value);
        return true;
    }

    /// <summary>
    /// Maps the closed relationship vocabulary. An unadmitted kind is a relationship error rather
    /// than a parse failure, and the ORGANIZATION_ACCOUNT kind is refused here because its owner has
    /// no admitted mutation contract.
    /// </summary>
    internal static bool TryIntent(
        QualifyLeadNurtureRequest request,
        out LeadNurtureContactIntent? intent,
        out Failure? error)
    {
        intent = null;
        error = null;
        var relationship = request.Relationship;
        if (relationship is null
            || !string.Equals(relationship.Kind, "CONTACT", StringComparison.Ordinal)
            || relationship.Mode is not ("NEW" or "EXISTING"))
        {
            error = new Failure("LEAD_QUALIFICATION_RELATIONSHIP_INVALID", StatusCodes.Status422UnprocessableEntity);
            return false;
        }

        // Presence is carried, not collapsed: whether the body declared a contact object and whether
        // it declared an organization object are both contract facts the coordinator must validate,
        // and neither survives a projection to nullable strings.
        intent = new LeadNurtureContactIntent(
            relationship.Mode == "NEW" ? LeadNurtureRelationshipMode.New : LeadNurtureRelationshipMode.Existing,
            relationship.SelectedId,
            relationship.Contact?.DisplayName,
            relationship.Contact?.Email,
            relationship.Contact?.Phone,
            relationship.Contact?.Title,
            ContactSupplied: relationship.Contact is not null,
            OrganizationSupplied: relationship.Organization is not null);
        return true;
    }

    internal static async Task<BodyRead<T>> ReadBodyAsync<T>(
        HttpContext context,
        string correlationId,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            var value = await context.Request.ReadFromJsonAsync<T>(StrictJson, cancellationToken);
            return value is null
                ? new(null, BodyError("A JSON request body is required.", correlationId))
                : new(value, null);
        }
        catch (JsonException)
        {
            return new(null, BodyError("The JSON request body does not match the contract.", correlationId));
        }
        catch (NotSupportedException)
        {
            return new(null, BodyError("A JSON request body is required.", correlationId));
        }
    }

    internal static IResult Error(Failure failure, string correlationId) =>
        Results.Json(
            new LeadQualificationProblemDetails(
                $"urn:unicore:error:{failure.Code.ToLowerInvariant()}",
                Title(failure.Code),
                failure.Status,
                failure.Code,
                false,
                correlationId,
                null,
                null,
                failure.FieldErrors,
                null,
                failure.ExpectedVersion,
                failure.CurrentVersion,
                failure.IdempotencyKey),
            statusCode: failure.Status,
            contentType: "application/problem+json");

    private static string Title(string code) => code switch
    {
        "VALIDATION_FAILED" => "Validation failed",
        "ACCESS_DENIED" => "Access denied",
        "WORKSPACE_MISMATCH" => "Workspace context mismatch",
        "RESOURCE_NOT_FOUND" => "Resource not found",
        "VERSION_CONFLICT" => "Version conflict",
        "IDEMPOTENCY_KEY_REUSED" => "Idempotency key reused",
        "LEAD_INVALID_TRANSITION" => "Lead transition is not allowed",
        "LEAD_QUALIFICATION_RELATIONSHIP_INVALID" => "Lead qualification relationship is invalid",
        "LEAD_QUALIFICATION_DOWNSTREAM_CAPABILITY_REQUIRED" => "Downstream capability required",
        "LEAD_QUALIFICATION_DOWNSTREAM_MODULE_DISABLED" => "Downstream module disabled",
        _ => "Internal error"
    };

    private static bool TryExpectedVersion(string supplied, out long? expectedVersion)
    {
        expectedVersion = null;
        var value = supplied.StartsWith("W/", StringComparison.Ordinal) ? supplied[2..] : supplied;
        if (value.Length < 3 || value[0] != '"' || value[^1] != '"')
            return false;
        if (!long.TryParse(value[1..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
            return false;
        expectedVersion = parsed;
        return true;
    }

    private static IResult BodyError(string message, string correlationId) =>
        Error(
            new Failure(
                "VALIDATION_FAILED",
                StatusCodes.Status422UnprocessableEntity,
                new Dictionary<string, string[]> { ["body"] = [message] }),
            correlationId);
}
