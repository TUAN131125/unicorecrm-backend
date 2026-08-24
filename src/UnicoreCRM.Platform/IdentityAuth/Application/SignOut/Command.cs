using UnicoreCRM.Platform.IdentityAuth.Application.Common;

namespace UnicoreCRM.Platform.IdentityAuth.Application.SignOut;

internal sealed record Command(string AccountId, string SessionId, string? Reason, RequestMetadata Metadata);
