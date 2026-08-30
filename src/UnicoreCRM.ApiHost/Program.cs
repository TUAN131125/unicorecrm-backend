using UnicoreCRM.Platform;
using UnicoreCRM.Crm;
using UnicoreCRM.Sales;
using UnicoreCRM.Billing;
using UnicoreCRM.Fulfillment;
using UnicoreCRM.Operations;
using UnicoreCRM.CommercialEvidence;
using UnicoreCRM.Workflows;
using UnicoreCRM.Integrations;
using UnicoreCRM.AI;
using UnicoreCRM.PlatformOperations;
using UnicoreCRM.Platform.IdentityAuth.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Operations.Tasks.Contracts;
using UnicoreCRM.Operations.Support.Contracts;
using UnicoreCRM.Crm.Leads.Contracts;
using UnicoreCRM.Crm.Deals.Contracts;
using UnicoreCRM.Crm.Contacts.Contracts;
using UnicoreCRM.Crm.Customers.Contracts;
using UnicoreCRM.Crm.Organizations.Contracts;
using UnicoreCRM.Sales.Products.Contracts;
using UnicoreCRM.Sales.Quotes.Contracts;
using UnicoreCRM.Integrations.Webhooks.Inbound.Contracts;
using UnicoreCRM.Workflows.Durable.Contracts;
using UnicoreCRM.AI.Gateway;
using UnicoreCRM.ApiHost.Development;
using UnicoreCRM.ApiHost.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.AddDevelopmentLocalConfiguration();

builder.AddDevelopmentDemoBootstrap();
// Registered before the modules so the owner-registered schema migrations run ahead of
// every owner Development seed. ApiHost invokes owner callbacks only; it holds no DbContext.
builder.Services.AddHostedService<DevelopmentSchemaMigrationService>();

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new UtcDateTimeOffsetJsonConverter()));

const string DevelopmentFrontendCors = "DevelopmentFrontend";
if (builder.Environment.IsDevelopment())
{
    var frontendOrigins = builder.Configuration.GetSection("Frontend:AllowedOrigins").Get<string[]>() ?? [];
    if (frontendOrigins.Length > 0)
    {
        builder.Services.AddCors(options => options.AddPolicy(DevelopmentFrontendCors, policy =>
            policy.WithOrigins(frontendOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials()));
    }
}

builder.Services.AddPlatformModule(builder.Configuration);
builder.Services.AddCrmModule(builder.Configuration);
builder.Services.AddSalesModule(builder.Configuration);
builder.Services.AddBillingModule();
builder.Services.AddFulfillmentModule();
builder.Services.AddOperationsModule(builder.Configuration);
builder.Services.AddCommercialEvidenceModule(builder.Configuration);
builder.Services.AddWorkflowsModule(builder.Configuration);
builder.Services.AddIntegrationsModule(builder.Configuration);
builder.Services.AddAIModule(builder.Configuration, builder.Environment);
builder.Services.AddPlatformOperationsModule(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler(errorApplication => errorApplication.Run(async context =>
{
    var suppliedCorrelationId = context.Request.Headers["X-Correlation-Id"].ToString();
    var correlationId = suppliedCorrelationId.Length is >= 8 and <= 128
        ? suppliedCorrelationId
        : context.TraceIdentifier;
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    context.Response.ContentType = "application/problem+json";
    await context.Response.WriteAsJsonAsync(new IdentityProblemDetails(
        "urn:unicore:error:internal_error",
        "Internal server error",
        StatusCodes.Status500InternalServerError,
        "INTERNAL_ERROR",
        false,
        correlationId));
}));
if (app.Environment.IsDevelopment()
    && app.Configuration.GetSection("Frontend:AllowedOrigins").Get<string[]>() is { Length: > 0 })
    app.UseCors(DevelopmentFrontendCors);
app.UseAuthentication();
app.UseTrustedWorkspaceResolution();
app.UseAuthorization();
app.MapIdentityAuthEndpoints();
app.MapWorkspaceEndpoints();
app.MapAccessControlEndpoints();
app.MapDurableWorkflowEndpoints();
app.MapTasksEndpoints();
app.MapSupportEndpoints();
app.MapLeadsEndpoints();
app.MapDealsEndpoints();
app.MapContactsEndpoints();
app.MapCustomersEndpoints();
app.MapOrganizationsEndpoints();
app.MapProductsEndpoints();
app.MapQuotesEndpoints();
app.MapInboundLeadWebhookEndpoints();
app.MapAiEndpoints();

app.Run();
