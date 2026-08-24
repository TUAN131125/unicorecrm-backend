namespace UnicoreCRM.Platform.IdentityAuth.Application.SignOut;

internal static class Validator
{
    internal static IReadOnlyDictionary<string, string[]> Validate(Command command)
    {
        if (command.Reason?.Length > 500)
            return new Dictionary<string, string[]> { ["reason"] = ["Reason must not exceed 500 characters."] };
        return new Dictionary<string, string[]>();
    }
}
