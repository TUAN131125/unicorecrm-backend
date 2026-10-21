using System.Text.Json.Serialization;

namespace UnicoreCRM.Communications.Contracts;

public sealed record TelegramConnectionDocument(string ChannelAccountId, string Provider, long ConfigurationVersion,
    string ConfigurationState, bool CredentialPresent, long CredentialGeneration, string VerificationState,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? BotId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DateTimeOffset? LastVerifiedAt,
    string Health, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? LastError,
    bool Enabled, bool SecurityReady, bool LiveAllowed = false, bool ProviderAllowed = false, bool Active = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DateTimeOffset? LastSuccessfulPollAt = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DateTimeOffset? LastCommittedUpdateAt = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DateTimeOffset? RetryAfter = null,
    long ConnectionGeneration = 0);
