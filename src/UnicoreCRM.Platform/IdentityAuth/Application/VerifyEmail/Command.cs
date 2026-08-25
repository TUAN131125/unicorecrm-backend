using UnicoreCRM.Platform.IdentityAuth.Application.Common;

namespace UnicoreCRM.Platform.IdentityAuth.Application.VerifyEmail;

internal sealed record Command(string Email, string Code, RequestMetadata Metadata);
