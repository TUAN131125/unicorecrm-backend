namespace UnicoreCRM.Platform.IdentityAuth.Application.SignIn;

internal static class Validator
{
    internal static IReadOnlyDictionary<string, string[]> Validate(Command command)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(command.Email) || command.Email.Length > 254)
            errors["email"] = ["Email is required and must not exceed 254 characters."];
        if (string.IsNullOrEmpty(command.Password) || command.Password.Length > 1024)
            errors["password"] = ["Password is required and must not exceed 1024 characters."];
        if (command.DeviceLabel?.Length > 160)
            errors["deviceLabel"] = ["Device label must not exceed 160 characters."];
        return errors;
    }
}
