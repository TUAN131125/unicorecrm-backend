using UnicoreCRM.Platform.IdentityAuth.Application.Common;

namespace UnicoreCRM.Platform.IdentityAuth.Application.RegisterAccount;

internal sealed record Command(string Email, string Password, string DisplayName, RequestMetadata Metadata);
