using UnicoreCRM.Communications.Contracts;

namespace UnicoreCRM.Communications.Application;

internal static class ChannelProviderCatalog
{
    internal static IReadOnlyList<ChannelProviderDocument> All { get; } =
    [
        Describe("gmail", "Gmail", "EMAIL"), Describe("telegram", "Telegram", "MESSAGING"),
        Describe("zalo", "Zalo OA", "MESSAGING"), Describe("messenger", "Messenger", "MESSAGING")
    ];
    private static ChannelProviderDocument Describe(string key, string name, string type) =>
        new(key, name, type, "SUPPORTED", "NOT_IMPLEMENTED", "NOT_IMPLEMENTED", "NOT_IMPLEMENTED");
}
