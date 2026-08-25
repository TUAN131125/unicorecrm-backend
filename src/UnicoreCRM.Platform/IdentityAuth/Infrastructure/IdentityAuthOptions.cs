using System.ComponentModel.DataAnnotations;

namespace UnicoreCRM.Platform.IdentityAuth.Infrastructure;

internal sealed class IdentityAuthOptions
{
    internal const string SectionName = "IdentityAuth";

    public JwtOptions Jwt { get; init; } = new();

    [Required, MinLength(32)]
    public string RefreshTokenPepper { get; init; } = string.Empty;

    public SessionOptions Session { get; init; } = new();
    public EmailVerificationOptions EmailVerification { get; init; } = new();
    public DevelopmentBootstrapOptions DevelopmentBootstrap { get; init; } = new();
}

internal sealed class JwtOptions
{
    [Required]
    public string Issuer { get; init; } = "UnicoreCRM";

    [Required]
    public string Audience { get; init; } = "UnicoreCRM.Api";

    [Required, MinLength(32)]
    public string SigningKey { get; init; } = string.Empty;

    [Range(1, 60)]
    public int AccessTokenMinutes { get; init; } = 15;
}

internal sealed class SessionOptions
{
    [Range(1, 90)]
    public int IdleDays { get; init; } = 30;

    [Range(1, 365)]
    public int AbsoluteDays { get; init; } = 90;
}

internal sealed class EmailVerificationOptions
{
    /// <summary>Code lifetime in minutes. The admitted contract window is five to ten minutes.</summary>
    [Range(5, 10)]
    public int ExpiryMinutes { get; init; } = 10;

    /// <summary>Verification attempts allowed against one issued code before it is spent.</summary>
    [Range(1, 10)]
    public int MaxAttempts { get; init; } = 5;

    /// <summary>Minimum interval between two issued codes for the same account.</summary>
    [Range(30, 3600)]
    public int ResendIntervalSeconds { get; init; } = 60;

    public EmailSenderOptions Sender { get; init; } = new();
}

internal sealed class EmailSenderOptions
{
    /// <summary>
    /// Sender selection. Only <c>DevelopmentLog</c> is implemented, and only the Development host
    /// environment may select it. Every other value, and every other environment, resolves the
    /// unavailable sender and fails closed until a real provider is implemented and configured.
    /// </summary>
    public string Kind { get; init; } = "Unavailable";
}

internal sealed class DevelopmentBootstrapOptions
{
    public bool Enabled { get; init; }
    public bool ApplyMigrations { get; init; }
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string DisplayName { get; init; } = "Development User";
}
