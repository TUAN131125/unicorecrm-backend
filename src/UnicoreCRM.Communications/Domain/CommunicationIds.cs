namespace UnicoreCRM.Communications.Domain;

/// <summary>
/// Communications owns every identifier for its sender, connection and message state.
/// Foreign owners may reference these identifiers but may not create them.
/// </summary>
internal static class CommunicationIds
{
    internal static string New(string prefix) => $"{prefix}_{Guid.NewGuid():N}";
}
