using System.ComponentModel.DataAnnotations;

namespace UnicoreCRM.Platform.IdentityAuth.Infrastructure;

internal sealed class IdentityAuthOptions
{
    internal const string SectionName = "IdentityAuth";

    public JwtOptions Jwt { get; init; } = new();

    [Required, MinLength(32)]
    public string RefreshTokenPepper { get; init; } = string.Empty;

    public SessionOptions Session { get; init; } = new();
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

internal sealed class DevelopmentBootstrapOptions
{
    public bool Enabled { get; init; }
    public bool ApplyMigrations { get; init; }
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string DisplayName { get; init; } = "Development User";
}
