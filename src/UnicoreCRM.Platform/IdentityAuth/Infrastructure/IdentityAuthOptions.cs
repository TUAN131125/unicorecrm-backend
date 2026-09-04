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
    public IdentityAbuseProtectionOptions AbuseProtection { get; init; } = new();
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
    public EmailOutboxOptions Outbox { get; init; } = new();
}

internal sealed class IdentityAbuseProtectionOptions
{
    public IdentityAbuseLimitOptions Registration { get; init; } = new(20, 5, 600);
    public IdentityAbuseLimitOptions VerificationRequest { get; init; } = new(30, 5, 600);
    public IdentityAbuseLimitOptions VerificationSubmission { get; init; } = new(60, 10, 600);
    public IdentityAbuseLimitOptions PasswordSignIn { get; init; } = new(60, 10, 300);
    public IdentityAbuseLimitOptions SessionRefresh { get; init; } = new(300, 60, 60);

    internal bool IsValid() =>
        Registration.IsValid()
        && VerificationRequest.IsValid()
        && VerificationSubmission.IsValid()
        && PasswordSignIn.IsValid()
        && SessionRefresh.IsValid();
}

internal sealed record IdentityAbuseLimitOptions(
    [property: Range(1, 100_000)] int OriginPermitLimit,
    [property: Range(1, 100_000)] int SubjectPermitLimit,
    [property: Range(1, 86_400)] int WindowSeconds)
{
    public IdentityAbuseLimitOptions() : this(1, 1, 60)
    {
    }

    internal bool IsValid() =>
        OriginPermitLimit is >= 1 and <= 100_000
        && SubjectPermitLimit is >= 1 and <= 100_000
        && WindowSeconds is >= 1 and <= 86_400;
}

internal sealed class EmailSenderOptions
{
    /// <summary>
    /// Sender selection: <c>GmailSmtp</c>, <c>DevelopmentLog</c>, <c>DevelopmentFailing</c> or
    /// <c>Unavailable</c>. The two <c>Development*</c> kinds are accepted only by a Development host.
    /// Every unrecognised value, and every non-Development host that asks for one of them, resolves
    /// the unavailable sender and fails closed.
    /// </summary>
    public string Kind { get; init; } = "Unavailable";

    public string Host { get; init; } = "smtp.gmail.com";

    public int Port { get; init; } = 587;

    public bool UseStartTls { get; init; } = true;

    /// <summary>SMTP account. Secret-adjacent: supply it from untracked local configuration.</summary>
    public string Username { get; init; } = string.Empty;

    /// <summary>Google App Password. A secret: never tracked, never logged, never echoed.</summary>
    public string AppPassword { get; init; } = string.Empty;

    public string FromAddress { get; init; } = string.Empty;

    public string FromName { get; init; } = "UnicoreCRM";

    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Development-only: an artificial pause, in milliseconds, before the console sender reports a
    /// send complete. It exists so a verification harness can hold a delivery attempt open long
    /// enough to observe the claim protecting it, and to drive several sequential sends past the
    /// duration of any single claim. Zero in every tracked configuration.
    /// </summary>
    public int SimulatedSendDelayMilliseconds { get; init; }

    /// <summary>
    /// Development-only, and meaningful only to the <c>DevelopmentFailing</c> sender: where that
    /// sender writes the provider error text it fabricates. It stands in for a provider's own
    /// transcript, so a verification harness can read the exact recipient, subject and code the
    /// simulated provider echoed and then prove none of them reached the outbox or the log. Empty in
    /// every tracked configuration.
    /// </summary>
    public string SimulatedFailureTranscriptPath { get; init; } = string.Empty;
}

/// <summary>
/// Delivery sweep policy for the IdentityAuth-owned email outbox. These values shape retries only;
/// they never affect whether a verification code is valid.
/// </summary>
internal sealed class EmailOutboxOptions
{
    /// <summary>Idle interval between dispatch passes. A committed message also signals the pass immediately.</summary>
    public int DispatchIntervalSeconds { get; init; } = 15;

    /// <summary>Delivery attempts before a message is abandoned. Unrelated to OTP verification attempts.</summary>
    public int MaxAttempts { get; init; } = 5;

    /// <summary>First retry delay. Later delays back off exponentially from this value.</summary>
    public int RetryBackoffSeconds { get; init; } = 30;

    /// <summary>Messages claimed per pass.</summary>
    public int BatchSize { get; init; } = 20;

    /// <summary>
    /// How long an individual delivery claim holds. Each message is claimed separately immediately
    /// before its own send, so this is the lifetime of one claim, never of a whole batch. The
    /// dispatcher raises it when it would otherwise be shorter than the sender timeout plus its
    /// safety margin, because a claim must always outlast the send it protects.
    /// </summary>
    public int LeaseSeconds { get; init; } = 120;
}

internal sealed class DevelopmentBootstrapOptions
{
    public bool Enabled { get; init; }
    public bool ApplyMigrations { get; init; }
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string DisplayName { get; init; } = "Development User";
}
