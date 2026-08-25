using System.Text.RegularExpressions;

namespace UnicoreCRM.Platform.IdentityAuth.Application.VerifyEmail;

internal static partial class Validator
{
    [GeneratedRegex("^[0-9]{6}$")]
    private static partial Regex VerificationCode();

    internal static IReadOnlyDictionary<string, string[]> Validate(Command command)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(command.Email) || command.Email.Length > 254 || !System.Net.Mail.MailAddress.TryCreate(command.Email, out _))
            errors["email"] = ["A valid email address of at most 254 characters is required."];
        if (command.Code is null || !VerificationCode().IsMatch(command.Code))
            errors["code"] = ["The verification code must contain exactly six digits."];
        return errors;
    }
}
