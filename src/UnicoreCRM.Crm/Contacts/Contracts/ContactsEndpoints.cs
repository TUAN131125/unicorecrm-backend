using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UnicoreCRM.Crm.Contacts.Application.Common;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Contacts.Contracts;

public static class ContactsEndpoints
{
    public static IEndpointRouteBuilder MapContactsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/contacts", ListContactsAsync)
            .RequireAuthorization().RequireTrustedWorkspace().WithName("listContacts");
        endpoints.MapGet("/contacts/{contactId}", GetContactAsync)
            .RequireAuthorization().RequireTrustedWorkspace().WithName("getContact");
        return endpoints;
    }

    private static async Task<IResult> ListContactsAsync(
        HttpContext context,
        Application.ListContacts.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!ContactsHttp.TryMetadata(context, out var metadata, out var error))
            return error!;
        var result = await handler.HandleAsync(new(metadata!), cancellationToken);
        return ContactsHttp.Result(result, metadata!.CorrelationId);
    }

    private static async Task<IResult> GetContactAsync(
        string contactId,
        HttpContext context,
        Application.GetContact.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!ContactsHttp.TryMetadata(context, out var metadata, out var error))
            return error!;
        var result = await handler.HandleAsync(new(contactId, metadata!), cancellationToken);
        return ContactsHttp.Result(result, metadata!.CorrelationId);
    }
}

internal static class ContactsHttp
{
    internal static bool TryMetadata(
        HttpContext context,
        out ContactRequestMetadata? metadata,
        out IResult? error)
    {
        metadata = null;
        error = null;
        var requestId = context.Request.Headers["X-Request-Id"].ToString();
        var suppliedCorrelation = context.Request.Headers["X-Correlation-Id"].ToString();
        var correlationId = suppliedCorrelation.Length is >= 8 and <= 128
            ? suppliedCorrelation
            : context.TraceIdentifier;
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (requestId.Length is < 8 or > 128)
            fields["X-Request-Id"] = ["X-Request-Id must contain between 8 and 128 characters."];
        if (suppliedCorrelation.Length != 0 && suppliedCorrelation.Length is < 8 or > 128)
            fields["X-Correlation-Id"] = ["X-Correlation-Id must contain between 8 and 128 characters."];
        if (fields.Count != 0)
        {
            error = Error(ContactErrors.Validation(fields, StatusCodes.Status400BadRequest), correlationId);
            return false;
        }

        context.Response.Headers["X-Correlation-Id"] = correlationId;
        metadata = new ContactRequestMetadata(requestId, correlationId);
        return true;
    }

    internal static IResult Result<T>(ContactOperationResult<T> result, string correlationId) =>
        result.IsSuccess ? Results.Json(result.Value) : Error(result.Error!, correlationId);

    private static IResult Error(ContactOperationError error, string correlationId) =>
        Results.Json(
            new ContactProblemDetails(
                $"urn:unicore:error:{error.Code.ToLowerInvariant()}",
                error.Title,
                error.Status,
                error.Code,
                false,
                correlationId,
                error.Detail,
                error.FieldErrors),
            statusCode: error.Status,
            contentType: "application/problem+json");
}

internal sealed record ContactProblemDetails(
    string Type,
    string Title,
    int Status,
    string Code,
    bool Retryable,
    string CorrelationId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Detail = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string[]>? FieldErrors = null);
