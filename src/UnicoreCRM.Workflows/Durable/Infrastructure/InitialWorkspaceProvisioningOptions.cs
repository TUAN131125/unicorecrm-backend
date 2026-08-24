namespace UnicoreCRM.Workflows.Durable.Infrastructure;

internal sealed class DurableWorkflowOptions
{
    internal const string SectionName = "Workflows";
    public InitialWorkspaceProvisioningOptions InitialWorkspaceProvisioning { get; init; } = new();
}

internal sealed class InitialWorkspaceProvisioningOptions
{
    /// <summary>Server-owned recovery cadence for anchors whose access assignment is outstanding.</summary>
    public bool ResumeEnabled { get; init; } = true;
    public int ResumeIntervalSeconds { get; init; } = 30;
    public int ResumeBatchSize { get; init; } = 50;
    public DurableWorkflowFaultInjectionOptions DevelopmentFaultInjection { get; init; } = new();
}

/// <summary>
/// Development-only fault injection used to prove partial-failure recovery against a real host.
/// Every switch is disabled by default and is ignored outside the Development environment.
/// </summary>
internal sealed class DurableWorkflowFaultInjectionOptions
{
    public bool FailAccessAssignment { get; init; }
}
