namespace UnicoreCRM.Operations.Tasks.Domain;

internal static class TaskIds
{
    internal static string New(string prefix) => $"{prefix}_{Guid.NewGuid():N}";
}
