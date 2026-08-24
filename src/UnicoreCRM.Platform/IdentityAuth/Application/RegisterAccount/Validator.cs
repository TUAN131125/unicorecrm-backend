namespace UnicoreCRM.Platform.IdentityAuth.Application.RegisterAccount;

internal static class Validator
{
    internal static IReadOnlyDictionary<string, string[]> Validate(Command command)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(command.Email) || command.Email.Length > 254 || !System.Net.Mail.MailAddress.TryCreate(command.Email, out _))
            errors["email"] = ["A valid email address of at most 254 characters is required."];
        if (string.IsNullOrEmpty(command.Password) || command.Password.Length is < 8 or > 1024)
            errors["password"] = ["Password must contain between 8 and 1024 characters."];
        if (string.IsNullOrWhiteSpace(command.DisplayName) || command.DisplayName.Length > 160)
            errors["displayName"] = ["Display name must contain between 1 and 160 characters."];
        return errors;
    }
}
