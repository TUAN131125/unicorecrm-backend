using System.Threading.RateLimiting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UnicoreCRM.Platform.IdentityAuth.Application.Common;

namespace UnicoreCRM.Platform.IdentityAuth.Infrastructure.Security;

internal sealed class InMemoryIdentityAbuseProtector : IIdentityAbuseProtector, IDisposable
{
    private readonly IdentityAuthOptions options;
    private readonly IIdentityRequestFingerprinter fingerprinter;
    private readonly IRefreshTokenProtector refreshTokenProtector;
    private readonly ILogger<InMemoryIdentityAbuseProtector> logger;
    private readonly PartitionedRateLimiter<PartitionRequest> limiter;

    public InMemoryIdentityAbuseProtector(
        IOptions<IdentityAuthOptions> options,
        IIdentityRequestFingerprinter fingerprinter,
        IRefreshTokenProtector refreshTokenProtector,
        ILogger<InMemoryIdentityAbuseProtector> logger)
    {
        this.options = options.Value;
        this.fingerprinter = fingerprinter;
        this.refreshTokenProtector = refreshTokenProtector;
        this.logger = logger;
        limiter = PartitionedRateLimiter.Create<PartitionRequest, string>(CreatePartition);
    }

    public IdentityAbuseDecision CheckOrigin(IdentityAbuseOperation operation, string origin) =>
        Check(operation, IdentityAbuseDimension.Origin, origin);

    public IdentityAbuseDecision CheckEmailSubject(IdentityAbuseOperation operation, string? email) =>
        Check(operation, IdentityAbuseDimension.Subject, (email ?? string.Empty).Trim().ToUpperInvariant());

    public IdentityAbuseDecision CheckRefreshSubject(string refreshToken)
    {
        var subject = refreshTokenProtector.HasExpectedShape(refreshToken, out var sessionId)
            ? sessionId
            : refreshToken;
        return Check(IdentityAbuseOperation.RefreshSession, IdentityAbuseDimension.Subject, subject);
    }

    public void Dispose() => limiter.Dispose();

    private RateLimitPartition<string> CreatePartition(PartitionRequest request)
    {
        var configuredLimit = GetLimit(request.Operation);
        var permitLimit = request.Dimension == IdentityAbuseDimension.Origin
            ? configuredLimit.OriginPermitLimit
            : configuredLimit.SubjectPermitLimit;

        return RateLimitPartition.GetFixedWindowLimiter(
            request.PartitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromSeconds(configuredLimit.WindowSeconds),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
                AutoReplenishment = true
            });
    }

    private IdentityAbuseDecision Check(
        IdentityAbuseOperation operation,
        IdentityAbuseDimension dimension,
        string identifier)
    {
        var partitionKey = fingerprinter.Create(
            "identity-abuse-protection",
            operation.ToString(),
            dimension.ToString(),
            identifier);
        using var lease = limiter.AttemptAcquire(new PartitionRequest(operation, dimension, partitionKey));
        if (lease.IsAcquired)
            return IdentityAbuseDecision.Allowed;

        var configuredWindow = TimeSpan.FromSeconds(GetLimit(operation).WindowSeconds);
        var retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out var reportedRetryAfter)
            ? reportedRetryAfter
            : configuredWindow;
        var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));

        logger.LogWarning(
            "IdentityAuth request throttled for {Operation} by {Dimension}; retry after {RetryAfterSeconds} seconds.",
            operation,
            dimension,
            retryAfterSeconds);

        return new IdentityAbuseDecision(false, TimeSpan.FromSeconds(retryAfterSeconds));
    }

    private IdentityAbuseLimitOptions GetLimit(IdentityAbuseOperation operation) => operation switch
    {
        IdentityAbuseOperation.RegisterAccount => options.AbuseProtection.Registration,
        IdentityAbuseOperation.RequestEmailVerification => options.AbuseProtection.VerificationRequest,
        IdentityAbuseOperation.VerifyEmail => options.AbuseProtection.VerificationSubmission,
        IdentityAbuseOperation.SignIn => options.AbuseProtection.PasswordSignIn,
        IdentityAbuseOperation.RefreshSession => options.AbuseProtection.SessionRefresh,
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
    };

    private sealed record PartitionRequest(
        IdentityAbuseOperation Operation,
        IdentityAbuseDimension Dimension,
        string PartitionKey);

    private enum IdentityAbuseDimension
    {
        Origin,
        Subject
    }
}
