namespace UnicoreCRM.Platform.AccessControl.Application.Common;

/// <summary>
/// A request body that the HTTP boundary has read under its explicit byte limit. The application
/// keeps the over-limit state so authorization and required request metadata retain precedence over
/// body-shape failures.
/// </summary>
internal sealed record AdministrativeRequestBody(string Value, bool ExceededLimit)
{
    internal static AdministrativeRequestBody TooLarge { get; } = new(string.Empty, true);
}
