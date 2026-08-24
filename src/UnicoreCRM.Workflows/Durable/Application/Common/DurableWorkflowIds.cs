namespace UnicoreCRM.Workflows.Durable.Application.Common;

internal static class DurableWorkflowIds
{
    internal static string New(string prefix) => $"{prefix}_{Guid.NewGuid():N}";
}
