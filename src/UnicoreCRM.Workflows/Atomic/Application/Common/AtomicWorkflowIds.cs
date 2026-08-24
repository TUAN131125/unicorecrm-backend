namespace UnicoreCRM.Workflows.Atomic.Application.Common;

internal static class AtomicWorkflowIds
{
    internal static string New(string prefix) => $"{prefix}_{Guid.NewGuid():N}";
}
