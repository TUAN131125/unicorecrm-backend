using UnicoreCRM.Platform.IdentityAuth.Application.Common;

namespace UnicoreCRM.Platform.IdentityAuth.Application.RefreshSession;

internal sealed record Command(string RefreshToken, RequestMetadata Metadata);
