using System.Text.Json.Serialization;
using System.Reflection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Frozen;
using System.Text.Json;
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
        endpoints.MapGet("/contacts/summary", ContactListSummaryAsync)
            .RequireAuthorization().RequireTrustedWorkspace().WithName("getContactListSummary");
        endpoints.MapGet("/contacts/{contactId}", GetContactAsync)
            .RequireAuthorization().RequireTrustedWorkspace().WithName("getContact");
        endpoints.MapPost("/contacts", CreateContactAsync)
            .RequireAuthorization().RequireTrustedWorkspace().WithName("createContact");
        endpoints.MapPatch("/contacts/{contactId}", UpdateContactAsync)
            .RequireAuthorization().RequireTrustedWorkspace().WithName("updateContact");
        endpoints.MapPost("/contacts/{contactId}/archive", ArchiveContactAsync)
            .RequireAuthorization().RequireTrustedWorkspace().WithName("archiveContact");
        endpoints.MapGet("/contacts/{contactId}/relationship-summary", GetRelationshipSummaryAsync)
            .RequireAuthorization().RequireTrustedWorkspace().WithName("getContactRelationshipSummary");
        endpoints.MapPost("/contacts/{contactId}/organization-relationships", CreateOrganizationRelationshipAsync)
            .RequireAuthorization().RequireTrustedWorkspace().WithName("createContactOrganizationRelationship");
        endpoints.MapPatch("/contacts/{contactId}/organization-relationships/{relationshipId}", UpdateOrganizationRelationshipAsync)
            .RequireAuthorization().RequireTrustedWorkspace().WithName("updateContactOrganizationRelationship");
        endpoints.MapPost("/contacts/{contactId}/organization-relationships/{relationshipId}/end", EndOrganizationRelationshipAsync)
            .RequireAuthorization().RequireTrustedWorkspace().WithName("endContactOrganizationRelationship");
        endpoints.MapPost("/contacts/{contactId}/customer-relationships", CreateCustomerRelationshipAsync)
            .RequireAuthorization().RequireTrustedWorkspace().WithName("createContactCustomerRelationship");
        endpoints.MapPatch("/contacts/{contactId}/customer-relationships/{relationshipId}", UpdateCustomerRelationshipAsync)
            .RequireAuthorization().RequireTrustedWorkspace().WithName("updateContactCustomerRelationship");
        endpoints.MapPost("/contacts/{contactId}/customer-relationships/{relationshipId}/end", EndCustomerRelationshipAsync)
            .RequireAuthorization().RequireTrustedWorkspace().WithName("endContactCustomerRelationship");
        return endpoints;
    }

    private static async Task<IResult> ListContactsAsync(
        HttpContext context,
        Application.ListContacts.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!ContactsHttp.TryMetadata(context, out var metadata, out var error))
            return error!;
        if (!TryListQuery(context, metadata!, out var query, out var invalid)) return invalid!;
        var result = await handler.HandleAsync(query!, cancellationToken);
        return ContactsHttp.Result(result, metadata!.CorrelationId);
    }

    private static async Task<IResult> ContactListSummaryAsync(HttpContext context, Application.ListContacts.Handler handler, CancellationToken cancellationToken)
    {
        if (!ContactsHttp.TryMetadata(context, out var metadata, out var error)) return error!;
        if (!TryListQuery(context, metadata!, out var query, out var invalid)) return invalid!;
        return ContactsHttp.Result(await handler.SummaryAsync(query!, cancellationToken), metadata!.CorrelationId);
    }

    private static bool TryListQuery(HttpContext context, ContactRequestMetadata metadata, out Application.ListContacts.Query? query, out IResult? error)
    {
        query = null; error = null;
        string? Value(string key) => context.Request.Query.TryGetValue(key, out var value) ? value.ToString() : null;
        var supported = new HashSet<string>(StringComparer.Ordinal) { "cursor", "limit", "search", "status", "ownerId", "ownerScope", "source", "relationshipLevel", "decisionRole", "doNotContact", "link", "sort", "nextFollowUpDate", "followUp" };
        var fields = new Dictionary<string, string[]>();
        foreach (var (key, values) in context.Request.Query)
            if (!supported.Contains(key) || values.Count != 1) fields[key] = ["Unsupported or repeated query parameter."];
        int? limit = null; bool? dnc = null;
        if (Value("limit") is { } rawLimit) { if (int.TryParse(rawLimit, out var parsed)) limit = parsed; else fields["limit"] = ["Invalid limit."]; }
        if (Value("doNotContact") is { } rawDnc) { if (bool.TryParse(rawDnc, out var parsed)) dnc = parsed; else fields["doNotContact"] = ["Invalid boolean."]; }
        if (fields.Count > 0) { error = ContactsHttp.Result(ContactOperationResult<object>.Failure(ContactErrors.Validation(fields)), metadata.CorrelationId); return false; }
        query = new(metadata, new(Value("search"), Value("status"), Value("ownerId"), Value("ownerScope"), Value("source"), Value("relationshipLevel"), Value("decisionRole"), dnc, Value("link"), Value("sort") ?? "recentlyUpdated", Value("nextFollowUpDate"), Value("followUp")), Value("cursor"), limit);
        return true;
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

    private static async Task<IResult> CreateContactAsync(HttpContext context, Application.CreateContact.Handler handler, CancellationToken cancellationToken)
    {
        if (!ContactsHttp.TryCommandMetadata(context, false, out var metadata, out var error)) return error!;
        var body = await ContactsHttp.ReadBodyAsync<CreateContactRequest>(context, metadata!.CorrelationId, cancellationToken);
        if (body.Error is not null) return body.Error;
        var result = await handler.HandleAsync(new(body.Value!, metadata), cancellationToken);
        return ContactsHttp.Result(result, metadata.CorrelationId, StatusCodes.Status201Created);
    }

    private static async Task<IResult> UpdateContactAsync(string contactId, HttpContext context, Application.UpdateContact.Handler handler, CancellationToken cancellationToken)
    {
        if (!ContactsHttp.TryCommandMetadata(context, true, out var metadata, out var error)) return error!;
        var body = await ContactsHttp.ReadUpdateBodyAsync(context, metadata!.CorrelationId, cancellationToken);
        if (body.Error is not null) return body.Error;
        var result = await handler.HandleAsync(new(contactId, body.Value!.Request, metadata, body.Value.SuppliedFields), cancellationToken);
        return ContactsHttp.Result(result, metadata.CorrelationId);
    }

    private static async Task<IResult> ArchiveContactAsync(string contactId, HttpContext context, Application.ArchiveContact.Handler handler, CancellationToken cancellationToken)
    {
        if (!ContactsHttp.TryCommandMetadata(context, true, out var metadata, out var error)) return error!;
        var body = await ContactsHttp.ReadBodyAsync<ArchiveContactRequest>(context, metadata!.CorrelationId, cancellationToken);
        if (body.Error is not null) return body.Error;
        var result = await handler.HandleAsync(new(contactId, body.Value!, metadata), cancellationToken);
        return ContactsHttp.Result(result, metadata.CorrelationId);
    }

    private static async Task<IResult> GetRelationshipSummaryAsync(string contactId, HttpContext context, Application.GetRelationshipSummary.Handler handler, CancellationToken cancellationToken)
    {
        if (!ContactsHttp.TryMetadata(context, out var metadata, out var error)) return error!;
        return ContactsHttp.Result(await handler.HandleAsync(new(contactId, metadata!), cancellationToken), metadata!.CorrelationId);
    }

    private static async Task<IResult> CreateOrganizationRelationshipAsync(string contactId, HttpContext context, Application.Relationships.Handler handler, CancellationToken cancellationToken)
    {
        if (!ContactsHttp.TryCommandMetadata(context, true, out var metadata, out var error)) return error!;
        var body = await ContactsHttp.ReadBodyAsync<CreateContactOrganizationRelationshipRequest>(context, metadata!.CorrelationId, cancellationToken);
        if (body.Error is not null) return body.Error;
        return ContactsHttp.Result(await handler.CreateOrganizationAsync(new(contactId, body.Value!, metadata), cancellationToken), metadata.CorrelationId);
    }

    private static async Task<IResult> UpdateOrganizationRelationshipAsync(string contactId, string relationshipId, HttpContext context, Application.Relationships.Handler handler, CancellationToken cancellationToken)
    {
        if (!ContactsHttp.TryCommandMetadata(context, true, out var metadata, out var error)) return error!;
        var body = await ContactsHttp.ReadBodyAsync<UpdateContactOrganizationRelationshipRequest>(context, metadata!.CorrelationId, cancellationToken);
        if (body.Error is not null) return body.Error;
        return ContactsHttp.Result(await handler.UpdateOrganizationAsync(new(contactId, relationshipId, body.Value!, metadata), cancellationToken), metadata.CorrelationId);
    }

    private static async Task<IResult> EndOrganizationRelationshipAsync(string contactId, string relationshipId, HttpContext context, Application.Relationships.Handler handler, CancellationToken cancellationToken)
    {
        if (!ContactsHttp.TryCommandMetadata(context, true, out var metadata, out var error)) return error!;
        var body = await ContactsHttp.ReadBodyAsync<EndContactRelationshipRequest>(context, metadata!.CorrelationId, cancellationToken);
        if (body.Error is not null) return body.Error;
        return ContactsHttp.Result(await handler.EndOrganizationAsync(new(contactId, relationshipId, body.Value!, metadata), cancellationToken), metadata.CorrelationId);
    }

    private static async Task<IResult> CreateCustomerRelationshipAsync(string contactId, HttpContext context, Application.Relationships.Handler handler, CancellationToken cancellationToken)
    {
        if (!ContactsHttp.TryCommandMetadata(context, true, out var metadata, out var error)) return error!;
        var body = await ContactsHttp.ReadBodyAsync<CreateContactCustomerRelationshipRequest>(context, metadata!.CorrelationId, cancellationToken);
        if (body.Error is not null) return body.Error;
        return ContactsHttp.Result(await handler.CreateCustomerAsync(new(contactId, body.Value!, metadata), cancellationToken), metadata.CorrelationId);
    }

    private static async Task<IResult> UpdateCustomerRelationshipAsync(string contactId, string relationshipId, HttpContext context, Application.Relationships.Handler handler, CancellationToken cancellationToken)
    {
        if (!ContactsHttp.TryCommandMetadata(context, true, out var metadata, out var error)) return error!;
        var body = await ContactsHttp.ReadBodyAsync<UpdateContactCustomerRelationshipRequest>(context, metadata!.CorrelationId, cancellationToken);
        if (body.Error is not null) return body.Error;
        return ContactsHttp.Result(await handler.UpdateCustomerAsync(new(contactId, relationshipId, body.Value!, metadata), cancellationToken), metadata.CorrelationId);
    }

    private static async Task<IResult> EndCustomerRelationshipAsync(string contactId, string relationshipId, HttpContext context, Application.Relationships.Handler handler, CancellationToken cancellationToken)
    {
        if (!ContactsHttp.TryCommandMetadata(context, true, out var metadata, out var error)) return error!;
        var body = await ContactsHttp.ReadBodyAsync<EndContactRelationshipRequest>(context, metadata!.CorrelationId, cancellationToken);
        if (body.Error is not null) return body.Error;
        return ContactsHttp.Result(await handler.EndCustomerAsync(new(contactId, relationshipId, body.Value!, metadata), cancellationToken), metadata.CorrelationId);
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

    internal static IResult Result<T>(ContactOperationResult<T> result, string correlationId, int successStatus) =>
        result.IsSuccess ? Results.Json(result.Value, statusCode: successStatus) : Error(result.Error!, correlationId);

    internal static bool TryCommandMetadata(HttpContext context, bool requireIfMatch, out ContactCommandMetadata? metadata, out IResult? error)
    {
        metadata = null;
        if (!TryMetadata(context, out var readMetadata, out error)) return false;
        var key = context.Request.Headers["Idempotency-Key"].ToString();
        var ifMatch = context.Request.Headers.IfMatch.ToString();
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (key.Length is < 8 or > 128) fields["Idempotency-Key"] = ["Idempotency-Key must contain between 8 and 128 characters."];
        long? expected = null;
        if (requireIfMatch && !TryExpectedVersion(ifMatch, out expected)) fields["If-Match"] = ["If-Match must contain a quoted non-negative resource version."];
        if (fields.Count != 0)
        {
            error = Error(ContactErrors.Validation(fields, StatusCodes.Status400BadRequest), readMetadata!.CorrelationId);
            return false;
        }
        metadata = new ContactCommandMetadata(readMetadata!.RequestId, readMetadata.CorrelationId, key, expected);
        return true;
    }

    internal static async Task<BodyRead<T>> ReadBodyAsync<T>(HttpContext context, string correlationId, CancellationToken cancellationToken) where T : class
    {
        try
        {
            var value = await context.Request.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
            return value is null
                ? new(null, Error(ContactErrors.Validation(new Dictionary<string, string[]> { ["body"] = ["A JSON request body is required."] }, 400), correlationId))
                : new(value, null);
        }
        catch (JsonException)
        {
            return new(null, Error(ContactErrors.Validation(new Dictionary<string, string[]> { ["body"] = ["The JSON request body is invalid."] }, 400), correlationId));
        }
    }

    internal static async Task<BodyRead<ContactUpdateBody>> ReadUpdateBodyAsync(HttpContext context, string correlationId, CancellationToken cancellationToken)
    {
        try
        {
            var options = context.RequestServices.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
            var document = await context.Request.ReadFromJsonAsync<JsonElement>(options, cancellationToken);
            if (document.ValueKind != JsonValueKind.Object) throw new JsonException();
            var request = document.Deserialize<UpdateContactRequest>(options);
            if (request is null) throw new JsonException();
            var comparer = options.PropertyNameCaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var names = typeof(UpdateContactRequest).GetProperties().ToDictionary(
                property => property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                    ?? options.PropertyNamingPolicy?.ConvertName(property.Name) ?? property.Name,
                property => JsonNamingPolicy.CamelCase.ConvertName(property.Name), comparer);
            var supplied = document.EnumerateObject().Select(property => names[property.Name]).ToFrozenSet(StringComparer.Ordinal);
            return new(new ContactUpdateBody(request, supplied), null);
        }
        catch (JsonException)
        {
            return new(null, Error(ContactErrors.Validation(new Dictionary<string, string[]> { ["body"] = ["The JSON request body is invalid."] }, 400), correlationId));
        }
    }

    private static bool TryExpectedVersion(string value, out long? version)
    {
        version = null;
        if (value.Length < 3 || value[0] != '"' || value[^1] != '"') return false;
        if (!long.TryParse(value[1..^1], out var parsed) || parsed < 0) return false;
        version = parsed;
        return true;
    }

    internal static IResult Error(ContactOperationError error, string correlationId) =>
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

internal sealed record ContactUpdateBody(UpdateContactRequest Request, IReadOnlySet<string> SuppliedFields);

internal sealed record BodyRead<T>(T? Value, IResult? Error);

internal sealed record ContactProblemDetails(
    string Type,
    string Title,
    int Status,
    string Code,
    bool Retryable,
    string CorrelationId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Detail = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string[]>? FieldErrors = null);
