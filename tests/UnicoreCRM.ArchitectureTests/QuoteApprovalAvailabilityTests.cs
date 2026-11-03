using System.Reflection;
using UnicoreCRM.Platform.AccessControl.Application.CheckQuoteApprovalAvailability;
using UnicoreCRM.Platform.AccessControl.Application.Common;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.AccessControl.Domain;
using UnicoreCRM.Platform.Workspace.Contracts;
using Xunit;

namespace UnicoreCRM.ArchitectureTests;

public sealed class QuoteApprovalAvailabilityTests
{
    private static readonly string[] Writes = ["approvalStatus", "approvedAt", "approvedBy"];
    private static readonly string[] Reads = ["title", "notes"];

    [Fact]
    public async Task Distinct_active_workspace_approver_is_available_without_business_or_audit_writes()
    {
        var f = Create();
        Assert.True(await Check(f));
        Assert.Equal(["approver-membership"], f.Persistence.LoadedMemberships);
        Assert.Equal(["approver"], f.Workspace.ResolvedMembers);
    }

    [Theory]
    [InlineData("creator")]
    [InlineData("requester")]
    public async Task Creator_and_requester_cannot_be_the_available_approver_even_with_workspace_capabilities(string candidate)
    {
        var f = Create(); f.Workspace.Candidates = [candidate];
        Assert.False(await Check(f));
        Assert.Empty(f.Workspace.ResolvedMembers);
        Assert.Empty(f.Persistence.LoadedMemberships);
    }

    [Theory]
    [InlineData("suspended")]
    [InlineData("foreign-directory")]
    [InlineData("foreign-member")]
    [InlineData("missing-active-member")]
    [InlineData("wrong-membership")]
    public async Task Membership_and_directory_must_verify_exact_active_workspace_identity(string condition)
    {
        var f = Create(); f.Workspace.Condition = condition;
        Assert.False(await Check(f));
        Assert.Empty(f.Persistence.LoadedMemberships);
    }

    [Theory]
    [InlineData("quotes.read")]
    [InlineData("quotes.approve")]
    public async Task Both_quote_read_and_approve_capabilities_are_required(string missing)
    {
        var f = Create(); f.Persistence.Capabilities = [.. f.Persistence.Capabilities.Where(value => value != missing)];
        Assert.False(await Check(f));
    }

    [Theory]
    [InlineData("Team")]
    [InlineData("Custom")]
    [InlineData("Own")]
    public async Task Candidate_scope_cannot_be_widened_to_workspace(string scope)
    {
        var f = Create(); f.Persistence.Scope = Enum.Parse<AccessDataScope>(scope);
        Assert.False(await Check(f));
    }

    [Fact]
    public async Task Own_scope_can_qualify_when_candidate_is_record_owner_but_not_creator_or_requester()
    {
        var f = Create(); f.Persistence.Scope = AccessDataScope.Own;
        Assert.True(await f.Reader.HasEligibleApproverAsync("ws", "approver", "creator", "requester", Writes, Reads, default));
    }

    [Theory]
    [InlineData("approvalStatus", "ReadOnly")]
    [InlineData("approvedBy", "Hidden")]
    [InlineData("approvedAt", "Masked")]
    [InlineData("title", "Hidden")]
    [InlineData("title", "Masked")]
    [InlineData("notes", "Hidden")]
    [InlineData("notes", "Masked")]
    public async Task Restricted_approval_write_or_required_read_fields_disqualify_candidate(string field, string access)
    {
        var f = Create(); f.Persistence.Restriction = new("quotes", field, Enum.Parse<AccessFieldAccess>(access));
        Assert.False(await Check(f));
    }

    [Fact]
    public async Task Restricted_optional_unrelated_read_field_does_not_disqualify_candidate()
    {
        var f = Create(); f.Persistence.Restriction = new("quotes", "recipientEmail", AccessFieldAccess.Hidden);
        Assert.True(await Check(f));
    }

    [Fact]
    public async Task Foreign_current_workspace_unknown_creator_and_unknown_field_fail_before_directory_read()
    {
        var f = Create();
        Assert.False(await f.Reader.HasEligibleApproverAsync("foreign", "owner", "creator", "requester", Writes, Reads, default));
        Assert.False(await f.Reader.HasEligibleApproverAsync("ws", "owner", "", "requester", Writes, Reads, default));
        Assert.False(await f.Reader.HasEligibleApproverAsync("ws", "owner", "creator", "requester", ["undeclaredField"], Reads, default));
        Assert.Equal(0, f.Workspace.DirectoryReads);
    }

    [Fact]
    public async Task Cancellation_propagates_before_loading_members_or_policy()
    {
        var f = Create();
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Reader.HasEligibleApproverAsync(
            "ws", "owner", "creator", "requester", Writes, Reads, cancelled.Token));
        Assert.Equal(0, f.Workspace.DirectoryReads);
        Assert.Empty(f.Persistence.LoadedMemberships);
    }

    private static Task<bool> Check(Fixture f) => f.Reader.HasEligibleApproverAsync("ws", "owner", "creator", "requester", Writes, Reads, default);
    private sealed record Fixture(QuoteApprovalAvailability Reader, WorkspacePorts Workspace, PersistenceSpy Persistence);
    private static Fixture Create()
    {
        var workspace = new WorkspacePorts();
        var persistence = DispatchProxy.Create<IAccessControlPersistence, PersistenceSpy>();
        return new(new(workspace, workspace, workspace, persistence, new([new QuoteFacts()])), workspace,
            (PersistenceSpy)(object)persistence);
    }

    private sealed class WorkspacePorts : ICurrentWorkspace, IWorkspaceAccessDirectorySource, ITrustedWorkspaceMemberResolver
    {
        internal string[] Candidates = ["creator", "requester", "approver"];
        internal string? Condition;
        internal int DirectoryReads;
        internal List<string> ResolvedMembers = [];
        public bool IsResolved => true;
        public TrustedWorkspaceContext Require() => new("ws", "requester-account", "requester", "requester-membership");
        public Task<WorkspaceAccessDirectorySnapshot?> ReadAsync(string workspaceId, CancellationToken ct)
        {
            DirectoryReads++;
            Assert.Equal("ws", workspaceId);
            var memberships = Candidates.Select(member => new WorkspaceAccessDirectoryMembership(member + "-membership",
                member + "-account", member, Condition == "suspended" ? "suspended" : "active", "test", null, 0, [])).ToArray();
            return Task.FromResult<WorkspaceAccessDirectorySnapshot?>(new(Condition == "foreign-directory" ? "foreign" : "ws",
                "workspace", "Workspace", "WS", memberships, []));
        }
        public Task<TrustedWorkspaceContext?> ResolveActiveMemberAsync(string workspaceId, string memberId, CancellationToken ct)
        {
            ResolvedMembers.Add(memberId);
            Assert.Equal("ws", workspaceId);
            return Task.FromResult<TrustedWorkspaceContext?>(Condition == "missing-active-member" ? null
                : new(Condition == "foreign-member" ? "foreign" : "ws", memberId + "-account", memberId,
                    Condition == "wrong-membership" ? "other-membership" : memberId + "-membership"));
        }
    }
    public class PersistenceSpy : DispatchProxy
    {
        internal string[] Capabilities = ["quotes.read", "quotes.approve"];
        internal AccessDataScope Scope = AccessDataScope.Workspace;
        internal EffectiveFieldSecurityPolicy? Restriction;
        internal List<string> LoadedMemberships = [];
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name != "LoadEffectiveStateAsync")
                throw new InvalidOperationException("Availability is read-only: " + method?.Name);
            Assert.Equal("ws", args![0]);
            LoadedMemberships.Add((string)args[1]!);
            return Task.FromResult(new EffectiveAccessState([], Capabilities, [new("quotes", Scope)],
                Restriction is null ? [] : [Restriction]));
        }
    }
    private sealed class QuoteFacts : IRecordAccessFactProvider
    {
        public RecordAccessResourceDescriptor Descriptor { get; } = RecordAccessResourceDescriptor.Create("quotes", "quotes.read",
            approveCapability: "quotes.approve", enforceableFields: new Dictionary<string, bool>
            { ["id"] = true, ["title"] = true, ["notes"] = false, ["recipientEmail"] = false, ["approvalStatus"] = false, ["approvedAt"] = false, ["approvedBy"] = false });
        public Task<RecordAccessFacts> ReadFactsAsync(TrustedWorkspaceContext workspace, string recordId,
            RecordAccessRequestContext context, CancellationToken ct) => throw new InvalidOperationException("Quotes supplies its owned facts; availability must not query foreign persistence.");
    }
}
