using UnicoreCRM.Platform.IdentityAuth.Application.Common;

namespace UnicoreCRM.Platform.IdentityAuth.Application.RequestEmailVerification;

internal sealed record Command(string Email, RequestMetadata Metadata);
