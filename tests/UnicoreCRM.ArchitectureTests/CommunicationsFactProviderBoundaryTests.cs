using System.Data;
using System.Data.Common;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UnicoreCRM.Communications;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;
using Xunit;

namespace UnicoreCRM.ArchitectureTests;

public sealed class CommunicationsFactProviderBoundaryTests
{
    private static readonly Assembly Communications = typeof(CommunicationsModule).Assembly;
    private const string Persistence = "UnicoreCRM.Communications.Infrastructure.Persistence.";

    [Fact]
    public void Scoped_fact_providers_preserve_separate_content_queue_and_management_authority()
    {
        var services = new ServiceCollection();
        services.AddCommunicationsModule(new ConfigurationBuilder().Build());
        var registrations = services.Where(x => x.ServiceType == typeof(IRecordAccessFactProvider)).ToArray();
        Assert.Equal(3, registrations.Length);
        foreach (var registration in registrations)
        {
            Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);
            Assert.Equal(Persistence.TrimEnd('.'), registration.ImplementationType!.Namespace);
        }

        using var context = Context(new EmptyReadInterceptor());
        var channels = Provider("ChannelAccessFacts", context).Descriptor;
        Assert.Equal("communications.channels.manage", channels.ReadCapability);
        Assert.Equal("communications.channels.manage", channels.UpdateCapability);
        Assert.All(channels.EnforceableFields.Values, required => Assert.True(required));
        Assert.DoesNotContain("text", channels.EnforceableFields.Keys);

        var content = Provider("ConversationAccessFacts", context).Descriptor;
        Assert.Equal("communications.read", content.ReadCapability);
        Assert.Equal("communications.transition", content.CommandCapabilities["communications.transition"]);
        Assert.Equal("communications.link", content.CommandCapabilities["communications.link"]);
        foreach (var field in new[] { "text", "crmLink", "crmContext" })
            Assert.False(content.EnforceableFields[field]);
        Assert.Null(content.UnassignedReadCapability);

        var queue = Provider("QueueAccessFacts", context).Descriptor;
        Assert.Equal("communications.queue.read", queue.ReadCapability);
        Assert.Equal("communications.queue.read", queue.UnassignedReadCapability);
        Assert.Equal("communications.claim", queue.UnassignedClaimCommand);
        Assert.Equal(new[] { "id", "lastActivityAt", "ownerId" }, queue.EnforceableFields.Keys.Order());
        Assert.False(queue.EnforceableFields["ownerId"]);
        Assert.DoesNotContain("text", queue.EnforceableFields.Keys);
    }

    [Theory]
    [InlineData("ChannelAccessFacts", "4bb191a2-2f5f-423c-9e2f-73a6d145a624", false)]
    [InlineData("ConversationAccessFacts", "42", true)]
    [InlineData("QueueAccessFacts", "42", true)]
    public async Task Database_fact_reads_keep_workspace_and_existing_assignee_predicates(
        string provider, string id, bool restrictAssignee)
    {
        var read = new EmptyReadInterceptor();
        using var context = Context(read);
        var result = await Provider(provider, context).ReadFactsAsync(
            new TrustedWorkspaceContext("workspace-a", "account-a", "member-a", "membership-a"), id, new("request-a", "correlation-a"), CancellationToken.None);

        Assert.Equal(RecordAccessFacts.NotFound, result);
        var sql = Assert.Single(read.Commands);
        Assert.Contains("[WorkspaceId] = @", sql.Text);
        Assert.Contains("workspace-a", sql.Parameters);
        Assert.Contains("[Id] = @", sql.Text);
        if (restrictAssignee)
        {
            Assert.Contains("[AssigneeMemberId] = @", sql.Text);
            Assert.Contains("member-a", sql.Parameters);
            Assert.Equal(provider == "QueueAccessFacts", sql.Text.Contains("[AssigneeMemberId] IS NULL OR", StringComparison.Ordinal));
        }
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData("ChannelAccessFacts")]
    [InlineData("ConversationAccessFacts")]
    [InlineData("QueueAccessFacts")]
    public async Task Malformed_record_identity_returns_not_found_before_database_access(string provider)
    {
        var read = new EmptyReadInterceptor();
        using var context = Context(read);
        var result = await Provider(provider, context).ReadFactsAsync(
            new("workspace-a", "account-a", "member-a", "membership-a"), "foreign/not-an-id", new("request-a", "correlation-a"), CancellationToken.None);
        Assert.Equal(RecordAccessFacts.NotFound, result);
        Assert.Empty(read.Commands);
    }

    private static IRecordAccessFactProvider Provider(string name, DbContext context) =>
        (IRecordAccessFactProvider)Activator.CreateInstance(Communications.GetType(Persistence + name, true)!,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [context], null)!;

    private static DbContext Context(DbCommandInterceptor capture) =>
        (DbContext)typeof(CommunicationsFactProviderBoundaryTests).GetMethod(nameof(TypedContext), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(Communications.GetType(Persistence + "CommunicationsDbContext", true)!).Invoke(null, [capture])!;

    private static DbContext TypedContext<TContext>(DbCommandInterceptor capture) where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>().UseSqlServer("Server=unused.invalid;Database=unused;Integrated Security=true;TrustServerCertificate=true")
            .AddInterceptors(new NoConnectionInterceptor(), capture).Options;
        return (DbContext)Activator.CreateInstance(typeof(TContext), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [options], null)!;
    }

    private sealed class NoConnectionInterceptor : DbConnectionInterceptor
    {
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,
            ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(InterceptionResult.Suppress());
    }

    // Inspect generated SQL and policy without opening a connection or modifying any database.
    private sealed class EmptyReadInterceptor : DbCommandInterceptor
    {
        internal List<(string Text, object?[] Parameters)> Commands { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add((command.CommandText, command.Parameters.Cast<DbParameter>().Select(x => x.Value).ToArray()));
            var table = new DataTable();
            if (command.CommandText.Contains("EXISTS", StringComparison.Ordinal))
            {
                table.Columns.Add("Value", typeof(bool));
                table.Rows.Add(false);
            }
            else table.Columns.Add("AssigneeMemberId", typeof(string));
            return ValueTask.FromResult(InterceptionResult<DbDataReader>.SuppressWithResult(table.CreateDataReader()));
        }
    }
}
