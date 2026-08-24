namespace UnicoreCRM.Platform.AccessControl.Domain;

internal static class AccessControlIds
{
    internal static string New(string prefix) => $"{prefix}_{Guid.NewGuid():N}";
}
