namespace UnicoreCRM.Platform.IdentityAuth.Application.RequestEmailVerification;

internal static class Validator
{
    internal static IReadOnlyDictionary<string, string[]> Validate(Command command)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(command.Email) || command.Email.Length > 254 || !System.Net.Mail.MailAddress.TryCreate(command.Email, out _))
            errors["email"] = ["A valid email address of at most 254 characters is required."];
        return errors;
    }
}
