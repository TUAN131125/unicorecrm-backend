using UnicoreCRM.Platform.IdentityAuth.Application.Common;

namespace UnicoreCRM.Platform.IdentityAuth.Application.SignIn;

internal sealed record Command(string Email, string Password, string? DeviceLabel, RequestMetadata Metadata);
