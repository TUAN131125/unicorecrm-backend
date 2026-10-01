namespace UnicoreCRM.Platform.Workspace.Contracts;

public sealed record LeadHandoverPolicy(int AcceptanceSlaHours);

/// <summary>Workspace-owned effective policy for the initial Lead Handover SLA resolution.</summary>
public interface ILeadHandoverPolicyReader
{
    Task<LeadHandoverPolicy?> FindAsync(string workspaceId, CancellationToken cancellationToken);
}
