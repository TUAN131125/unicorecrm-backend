namespace UnicoreCRM.Platform.IdentityAuth.Application.GetCurrentSession;

internal sealed record Query(string AccountId, string SessionId, string CorrelationId);
