namespace UnicoreCRM.Communications.Application;

internal static class CommunicationsAccessFields
{
    internal static readonly IReadOnlyDictionary<string, bool> Channels = new[]
    {
        "id", "providerKey", "channelType", "displayName", "status", "configurationVersion", "createdAt", "updatedAt", "externalAccountKey", "lastVerifiedAt"
    }.ToDictionary(x => x, _ => true);

    internal static readonly IReadOnlyDictionary<string, bool> Conversations = new Dictionary<string, bool>
    {
        ["id"] = true, ["lastActivityAt"] = true, ["version"] = true,
        ["conversationId"] = true, ["sequence"] = true, ["occurredAt"] = true, ["text"] = false, ["status"] = true,
        ["crmLink"] = false, ["crmContext"] = false
    };

    internal static readonly IReadOnlyDictionary<string, bool> Queue = new Dictionary<string, bool>
    {
        ["id"] = true, ["lastActivityAt"] = true, ["ownerId"] = false
    };
}
