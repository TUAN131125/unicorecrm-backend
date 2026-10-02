using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
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
using UnicoreCRM.Workflows.Atomic.Contracts;
using UnicoreCRM.Workflows.Atomic.Application.HandoverLead;
using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Platform.Workspace.Application.Common;
using UnicoreCRM.Platform.Workspace.Infrastructure.Persistence;
using UnicoreCRM.Platform.Workspace.Domain;
using UnicoreCRM.Crm.Leads.Contracts;
using UnicoreCRM.Workflows.Atomic.Infrastructure.Persistence;

// Test-only composition root. All authorization, persistence and participants are production services.
// Only the fault injector and scheduling of the real recovery runner are controlled by this verifier.
var seedStudio = args.Contains("--seed-studio", StringComparer.Ordinal);
var builder = WebApplication.CreateBuilder(args.Where(arg => arg != "--seed-studio").ToArray());
var connection = builder.Configuration.GetConnectionString("UnicoreCRM") ?? throw new InvalidOperationException("Missing connection.");
var database = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connection).InitialCatalog;
if (!System.Text.RegularExpressions.Regex.IsMatch(database, "^UnicoreCRM_O2Claim_O4_[A-Za-z0-9_]+$"))
    throw new InvalidOperationException("The verifier requires an isolated O4 database.");
var controlKey = builder.Configuration["LeadHandoverVerifier:ControlKey"] ?? (seedStudio ? "seed-only" : throw new InvalidOperationException("Missing verifier control key."));
builder.Services.AddPlatformModule(builder.Configuration);
builder.Services.AddCrmModule(builder.Configuration);
builder.Services.AddSalesModule(builder.Configuration);
builder.Services.AddBillingModule(builder.Configuration);
builder.Services.AddFulfillmentModule();
builder.Services.AddOperationsModule(builder.Configuration);
builder.Services.AddCommercialEvidenceModule(builder.Configuration);
builder.Services.AddWorkflowsModule(builder.Configuration);
builder.Services.AddIntegrationsModule(builder.Configuration);
builder.Services.AddAIModule(builder.Configuration, builder.Environment);
builder.Services.AddPlatformOperationsModule(builder.Configuration);
// Manual recovery makes the irreversible boundary inspectable without a background scan winning the race.
builder.Services.RemoveAll<IHostedService>();
builder.Services.RemoveAll<ILeadHandoverFaultInjector>();
builder.Services.AddSingleton<ILeadHandoverFaultInjector>(new CommitFault(builder.Configuration.GetValue<bool>("LeadHandoverVerifier:InjectFault")));
var app = builder.Build();
if (seedStudio)
{
    var workspaceId = builder.Configuration["LeadHandoverVerifier:WorkspaceId"] ?? throw new InvalidOperationException("Missing fixture workspace.");
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<WorkspaceDbContext>();
    if (!await db.Set<StudioConfiguration>().AnyAsync(x => x.WorkspaceId == workspaceId))
    {
        var defaults = StudioDefaults.Create(workspaceId, "O4 verifier", "en", "UTC", "USD", ["leads", "tasks", "deals"], DateTimeOffset.UtcNow);
        db.Add(defaults.Configuration);
        db.Add(defaults.QuickSetup);
        await db.SaveChangesAsync();
    }
    await app.DisposeAsync();
    return;
}
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    context.Response.StatusCode = 500;
    await context.Response.WriteAsJsonAsync(new { code = "VERIFIER_INJECTED_FAILURE" });
}));
app.UseAuthentication();
app.UseTrustedWorkspaceResolution();
app.UseAuthorization();
app.MapIdentityAuthEndpoints();
app.MapLeadHandoverEndpoints();
// This route exists only in this executable, is loopback-only, and still invokes service authorization.
app.MapPost("/__verifier/recover", async (HttpContext context, ILeadHandoverRecoveryRunner runner, CancellationToken ct) =>
{
    if (context.Connection.RemoteIpAddress is null || !System.Net.IPAddress.IsLoopback(context.Connection.RemoteIpAddress)
        || context.Request.Headers["X-Verifier-Control"].ToString() != controlKey) return Results.StatusCode(403);
    return Results.Json(new { completed = await runner.ResumeDueAsync(ct) });
});

// Real SQL ordering: a competing worker acquires its lease after the deferral read.
app.MapPost("/__verifier/deferral-concurrency/{handoverId}", async (string handoverId, HttpContext context,
    IServiceScopeFactory scopes, CancellationToken ct) =>
{
    if (context.Connection.RemoteIpAddress is null || !System.Net.IPAddress.IsLoopback(context.Connection.RemoteIpAddress)
        || context.Request.Headers["X-Verifier-Control"].ToString() != controlKey) return Results.StatusCode(403);
    await using var deferredScope = scopes.CreateAsyncScope();
    await using var workerScope = scopes.CreateAsyncScope();
    var deferredDb = deferredScope.ServiceProvider.GetRequiredService<WorkflowsDbContext>();
    var workerDb = workerScope.ServiceProvider.GetRequiredService<WorkflowsDbContext>();
    var deferred = await deferredDb.LeadHandoverAnchors.SingleAsync(x => x.HandoverId == handoverId, ct);
    var worker = await workerDb.LeadHandoverAnchors.SingleAsync(x => x.HandoverId == handoverId, ct);
    var stage = worker.Stage;
    var now = DateTimeOffset.UtcNow;
    if (!deferred.DeferRecoveryAccessDenied(now)) return Results.Conflict();
    worker.AcquireLease("verifier_competing_worker", "svc_lead_handover_recovery", now, TimeSpan.FromMinutes(2));
    await workerDb.SaveChangesAsync(ct);
    var conflictCaught = false;
    try { await deferredDb.SaveChangesAsync(ct); }
    catch (DbUpdateConcurrencyException) { conflictCaught = true; }
    workerDb.ChangeTracker.Clear();
    var current = await workerDb.LeadHandoverAnchors.SingleAsync(x => x.HandoverId == handoverId, ct);
    return Results.Json(new { conflictCaught, leasePreserved = current.OwnsLease("verifier_competing_worker", now),
        stagePreserved = current.Stage == stage, activeLeaseGuard = !current.DeferRecoveryAccessDenied(now) });
});
app.MapPost("/__verifier/lead-reservation", async (ReservationProbe body, HttpContext context,
    ICurrentWorkspace current, ILeadHandoverParticipant participant, CancellationToken ct) =>
{
    if (context.Connection.RemoteIpAddress is null || !System.Net.IPAddress.IsLoopback(context.Connection.RemoteIpAddress)
        || context.Request.Headers["X-Verifier-Control"].ToString() != controlKey) return Results.StatusCode(403);
    var trusted = current.Require();
    var service = body.Operation is "resolve" or "release";
    var command = new LeadHandoverParticipantCommand(trusted, body.LeadId, body.HandoverId,
        trusted.MemberId, body.NextOwnerId, "Reservation fence fixture", 0,
        body.HandoverId + (body.Operation == "release" ? ":release" : ":reserve"),
        "req-verifier-lead-fence", "corr-verifier-lead-fence", trusted.MemberId,
        service ? "svc_lead_handover_recovery" : trusted.MemberId);
    var result = body.Operation switch
    {
        "resolve" => await participant.ResolveReservationOrFenceAsync(command, ct),
        "reserve" => await participant.ReserveAsync(command, ct),
        "release" => await participant.ReleaseAsync(command, ct),
        _ => new LeadHandoverParticipantResult(false, null, "INVALID_PROBE", 422)
    };
    return Results.Json(result, statusCode: result.IsSuccess ? 200 : result.ErrorStatus ?? 500);
}).RequireAuthorization().RequireTrustedWorkspace();
app.Run();

internal sealed record ReservationProbe(string LeadId, string HandoverId, string NextOwnerId, string Operation);

internal sealed class CommitFault(bool enabled) : ILeadHandoverFaultInjector
{
    private int fired;
    public Task AfterParticipantCommitAsync(LeadHandoverFaultPoint point, CancellationToken ct)
    {
        if (enabled && point == LeadHandoverFaultPoint.AfterTasksCommit && Interlocked.Exchange(ref fired, 1) == 0)
            throw new InvalidOperationException("Deliberate verifier failure after real Tasks commit, before anchor/Lead completion.");
        return Task.CompletedTask;
    }
}
