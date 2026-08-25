using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UnicoreCRM.Platform.IdentityAuth.Application.Common;
using UnicoreCRM.Platform.IdentityAuth.Domain;
using UnicoreCRM.Platform.IdentityAuth.Infrastructure.Persistence;

namespace UnicoreCRM.Platform.IdentityAuth.Infrastructure.Email;

/// <summary>
/// Delivers the IdentityAuth email outbox after the issuing transaction has committed.
///
/// Remote SMTP is slow and fails in ways a database transaction must not wait for, so the request
/// path only stages a durable message. This dispatcher claims due messages, sends them and records
/// the outcome, all outside the transaction that produced them.
///
/// <b>Claiming is per message, never per batch.</b> A pass first reads a bounded set of due
/// candidates without locking or claiming anything; that list only decides what to <em>attempt</em>.
/// Each candidate is then re-read, re-checked and claimed in its own small serializable transaction
/// <em>immediately before its own send</em>, and the send is bounded by the claim that transaction
/// just committed. A batch-wide claim cannot express this: messages are delivered sequentially, so
/// one timestamp shared by twenty messages is already stale by the time the later ones start, and a
/// send running under a lapsed claim is exactly the thing a resend is entitled to assume cannot
/// happen.
///
/// The claim commits before any network call, so it is visible to the issuing transaction: a resend
/// that sees a live claim declines to revoke the code rather than leaving an email in flight for a
/// challenge it just superseded. The claim also outlives any send it protects, because the effective
/// lease is never shorter than the sender's own timeout plus a safety margin and the send is
/// additionally capped at the remaining lease.
///
/// A message carries a credential, so its challenge is re-checked inside the claim transaction and
/// the message is retired with no network call if the challenge is no longer eligible. That check
/// runs <em>before</em> the claim, so a message that is never sent never spends a delivery attempt.
///
/// Delivery is at-least-once - a crash between a successful send and its outcome write can repeat an
/// email - but a repeat can only ever resend the same code, because the message is keyed one-to-one
/// to its challenge and creates no account or challenge state of its own.
///
/// It logs identifiers, attempt counts and bounded failure codes only: never a recipient address,
/// never a code, never a credential, and never provider-authored text.
/// </summary>
internal sealed class IdentityEmailOutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    IIdentityEmailSender sender,
    IIdentityEmailPayloadProtector payloads,
    IdentityEmailOutboxSignal signal,
    IOptions<IdentityAuthOptions> options,
    TimeProvider timeProvider,
    ILogger<IdentityEmailOutboxDispatcher> logger) : BackgroundService
{
    /// <summary>
    /// How much longer a claim must outlast the send it protects. It absorbs the time between
    /// committing the claim and entering the sender, and the time between the sender returning and
    /// the outcome being written.
    /// </summary>
    private static readonly TimeSpan ClaimSafetyMargin = TimeSpan.FromSeconds(30);

    private const string ClaimExpiredBeforeSend = "CLAIM_EXPIRED_BEFORE_SEND";

    private bool reportedUnavailable;

    private EmailOutboxOptions Outbox => options.Value.EmailVerification.Outbox;

    private TimeSpan Interval => TimeSpan.FromSeconds(Math.Clamp(Outbox.DispatchIntervalSeconds, 1, 300));

    private TimeSpan SenderTimeout => TimeSpan.FromSeconds(Math.Clamp(options.Value.EmailVerification.Sender.TimeoutSeconds, 1, 300));

    /// <summary>
    /// The lease every individual claim is granted, measured from the moment that claim commits.
    ///
    /// It is never shorter than <c>senderTimeout + ClaimSafetyMargin</c>, because the claim is what
    /// tells the rest of the owner that a code is still on its way: a lease that could lapse while
    /// its own send is still running would let a resend revoke a code that is about to arrive.
    /// </summary>
    private TimeSpan EffectiveLease
    {
        get
        {
            var configured = TimeSpan.FromSeconds(Math.Clamp(Outbox.LeaseSeconds, 30, 3600));
            var floor = SenderTimeout + ClaimSafetyMargin;
            return configured > floor ? configured : floor;
        }
    }

    private TimeSpan BaseBackoff => TimeSpan.FromSeconds(Math.Clamp(Outbox.RetryBackoffSeconds, 5, 3600));

    private int BatchSize => Math.Clamp(Outbox.BatchSize, 1, 200);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is IdentityEmailDeliveryFailedException or IdentityEmailSenderUnavailableException)
            {
                // These two are the only exceptions in this loop that can have been shaped by a
                // provider, so only their bounded classification is recorded - never their text.
                logger.LogError(
                    "The identity email outbox pass failed at the delivery boundary: {Reason}",
                    exception is IdentityEmailDeliveryFailedException failed ? failed.ReasonCode : EmailOutboxReasons.SenderUnavailable);
            }
            catch (Exception exception)
            {
                // A failed pass must never end the dispatcher: the next pass retries the same rows.
                // Nothing provider-authored reaches here - delivery faults are caught above and per
                // message below - so this is the owner's own infrastructure fault, logged in full.
                logger.LogError(exception, "The identity email outbox pass failed.");
            }

            await signal.WaitAsync(Interval, stoppingToken);
        }
    }

    private async Task DispatchDueAsync(CancellationToken cancellationToken)
    {
        try
        {
            sender.EnsureConfigured();
            reportedUnavailable = false;
        }
        catch (IdentityEmailSenderUnavailableException)
        {
            // Burning delivery attempts against a host that cannot send at all would abandon
            // messages for a configuration problem. Hold them instead, and say so once. The reason
            // is a bounded code, not the exception text.
            if (!reportedUnavailable)
            {
                reportedUnavailable = true;
                logger.LogWarning("The identity email outbox is holding messages: {Reason}", EmailOutboxReasons.SenderUnavailable);
            }

            return;
        }

        foreach (var candidate in await FindDueCandidatesAsync(cancellationToken))
        {
            await DeliverCandidateAsync(candidate, cancellationToken);
        }
    }

    /// <summary>
    /// The due messages this pass will try, as identifiers only.
    ///
    /// Nothing is claimed and no lock is held here: by the time a later candidate is reached its row
    /// may well have been superseded, retired or claimed by someone else, so this list is a plan,
    /// not a decision. Every entry is re-validated inside its own claim transaction.
    /// </summary>
    private async Task<IReadOnlyList<OutboxCandidate>> FindDueCandidatesAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityAuthDbContext>();
        return await dbContext.EmailOutboxMessages
            .AsNoTracking()
            .Where(x => x.Status == EmailOutboxStatus.Pending
                && x.NextAttemptAt <= now
                && (x.LeasedUntil == null || x.LeasedUntil <= now))
            .OrderBy(x => x.CreatedAt)
            .Take(BatchSize)
            .Select(x => new OutboxCandidate(x.MessageId, x.ChallengeId))
            .ToListAsync(cancellationToken);
    }

    private async Task DeliverCandidateAsync(OutboxCandidate candidate, CancellationToken cancellationToken)
    {
        // One scope, and therefore one change tracker, per message: a claim and its outcome are the
        // only things this context ever holds, so no other message's state can be dragged into the
        // save that resolves this one.
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityAuthDbContext>();
        var claim = await TryClaimAsync(dbContext, candidate, cancellationToken);
        if (claim is null)
        {
            return;
        }

        await SendClaimedAsync(dbContext, claim.Message, claim.ExpiresAt, cancellationToken);
    }

    /// <summary>
    /// Re-reads one candidate, decides whether it may still be delivered, and claims it for exactly
    /// one attempt. Returns <c>null</c> when nothing should be sent, which covers a row that has
    /// gone terminal, is not due after all, is already claimed by another pass, or has just been
    /// retired here because its challenge is no longer eligible.
    ///
    /// The challenge is read before the message, which is the order the issuing and verifying
    /// transactions also use. Consistent ordering is what keeps a resend racing this claim to a
    /// block rather than a deadlock.
    /// </summary>
    private async Task<OutboxClaim?> TryClaimAsync(
        IdentityAuthDbContext dbContext,
        OutboxCandidate candidate,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var challenge = await dbContext.EmailVerificationChallenges
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.ChallengeId == candidate.ChallengeId, cancellationToken);
        var message = await dbContext.EmailOutboxMessages
            .SingleOrDefaultAsync(x => x.MessageId == candidate.MessageId, cancellationToken);
        if (message is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        if (!message.IsPending || message.NextAttemptAt > now || message.IsDeliveryInFlight(now))
        {
            return null;
        }

        if (ResolveIneligibility(challenge, message, now) is { } reason)
        {
            // Fail closed before any network I/O, and before the claim, so a message that is never
            // sent never spends one of its delivery attempts.
            message.Cancel(now, reason);
            if (!await TryResolveAsync(dbContext, message, "cancelled before delivery", cancellationToken))
            {
                return null;
            }

            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation(
                "Identity email {MessageId} for challenge {ChallengeId} was cancelled before delivery: {Reason}",
                message.MessageId,
                message.ChallengeId,
                reason);
            return null;
        }

        message.Lease(now, EffectiveLease);
        if (!await TryResolveAsync(dbContext, message, "claimed", cancellationToken))
        {
            return null;
        }

        await transaction.CommitAsync(cancellationToken);
        // The claim's own committed expiry is the one the send is bounded by. Nothing recomputes it.
        return new OutboxClaim(message, message.LeasedUntil!.Value);
    }

    private async Task SendClaimedAsync(
        IdentityAuthDbContext dbContext,
        IdentityEmailOutboxMessage message,
        DateTimeOffset claimExpiresAt,
        CancellationToken cancellationToken)
    {
        if (message.ProtectedCode is null || !payloads.TryUnprotect(message.ChallengeId, message.ProtectedCode, out var code))
        {
            await TryResolveAsync(dbContext, message, "abandoned", cancellationToken, m => m.Abandon(timeProvider.GetUtcNow(), EmailOutboxReasons.PayloadUnreadable));
            logger.LogError(
                "Identity email {MessageId} was abandoned: {Reason}.",
                message.MessageId,
                EmailOutboxReasons.PayloadUnreadable);
            return;
        }

        var remaining = claimExpiresAt - timeProvider.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        {
            // The claim lapsed between committing it and reaching this line. Calling the sender now
            // would put an email in flight that no durable claim protects, and a resend would be
            // entitled to revoke its code while it was still on its way. Give the claim up instead;
            // the attempt already pushed the next one out, so a later pass claims it afresh.
            await TryResolveAsync(dbContext, message, "released", cancellationToken, m => m.ReleaseClaim());
            logger.LogWarning(
                "Identity email {MessageId} released its claim without sending on attempt {Attempt}: {Reason}",
                message.MessageId,
                message.AttemptCount,
                ClaimExpiredBeforeSend);
            return;
        }

        // The send can never outlast the claim that protects it from supersession.
        using var send = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        send.CancelAfter(remaining);
        try
        {
            await sender.SendEmailVerificationCodeAsync(
                new IdentityEmailVerificationMessage(message.Recipient, message.DisplayName, code, message.CodeExpiresAt),
                send.Token);
            if (await TryResolveAsync(dbContext, message, "sent", cancellationToken, m => m.MarkSent(timeProvider.GetUtcNow())))
            {
                logger.LogInformation(
                    "Identity email {MessageId} for account {AccountId} was accepted by the provider on attempt {Attempt}.",
                    message.MessageId,
                    message.AccountId,
                    message.AttemptCount);
            }

            // On a conflict the resolution above has already reported which committed state was kept,
            // so a second "accepted" line here would only argue with it.
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            // The claim's deadline fired rather than the host stopping: a timed-out attempt.
            await RecordFailureAsync(dbContext, message, EmailOutboxReasons.SmtpTimeout, cancellationToken);
        }
        catch (IdentityEmailDeliveryFailedException exception)
        {
            await RecordFailureAsync(dbContext, message, exception.ReasonCode, cancellationToken);
        }
        catch (IdentityEmailSenderUnavailableException)
        {
            await RecordFailureAsync(dbContext, message, EmailOutboxReasons.SenderUnavailable, cancellationToken);
        }
        catch (Exception)
        {
            // An unexpected provider fault is still just a failed attempt. Nothing about the
            // exception is kept: a sender that has not classified its own fault has not been through
            // any scrubbing, and even its type name is provider-chosen.
            await RecordFailureAsync(dbContext, message, EmailOutboxReasons.UnknownDeliveryFailure, cancellationToken);
        }
    }

    /// <summary>
    /// Answers why this message must not be delivered, or <c>null</c> when it still may be.
    /// </summary>
    private static string? ResolveIneligibility(
        IdentityEmailVerificationChallenge? challenge,
        IdentityEmailOutboxMessage message,
        DateTimeOffset now)
    {
        if (message.CodeExpiresAt <= now)
        {
            return EmailOutboxReasons.CodeExpiredBeforeDelivery;
        }

        if (challenge is null)
        {
            return EmailOutboxReasons.ChallengeNotDeliverable;
        }

        if (challenge.SupersededAt is not null)
        {
            return EmailOutboxReasons.ChallengeSuperseded;
        }

        if (challenge.ConsumedAt is not null)
        {
            return EmailOutboxReasons.ChallengeConsumed;
        }

        return challenge.IsDeliverable(now) ? null : EmailOutboxReasons.ChallengeExpired;
    }

    /// <summary>
    /// Applies a resolution to a message and saves it, refusing to overwrite a newer committed
    /// state.
    ///
    /// The row carries a rowversion, so a write built on a stale image fails instead of silently
    /// winning. On conflict the row is reloaded and whatever the other writer committed is left
    /// exactly as it stands - a retry here would be a stale dispatcher stamping <c>Sent</c> over a
    /// <c>Cancelled</c> row and re-asserting that a revoked code was delivered. This is
    /// defence in depth: correct per-message claiming is what prevents the conflict arising.
    /// </summary>
    private async Task<bool> TryResolveAsync(
        IdentityAuthDbContext dbContext,
        IdentityEmailOutboxMessage message,
        string outcome,
        CancellationToken cancellationToken,
        Action<IdentityEmailOutboxMessage>? resolution = null)
    {
        resolution?.Invoke(message);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            var current = await dbContext.EmailOutboxMessages
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.MessageId == message.MessageId, cancellationToken);
            logger.LogWarning(
                "Identity email {MessageId} could not be recorded as {Outcome} on attempt {Attempt}: another writer committed {CommittedStatus} first, and that state is preserved.",
                message.MessageId,
                outcome,
                message.AttemptCount,
                current is null ? "Deleted" : current.Status.ToString());
            return false;
        }
    }

    private async Task RecordFailureAsync(
        IdentityAuthDbContext dbContext,
        IdentityEmailOutboxMessage message,
        string reasonCode,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        // Exponential backoff from the configured base, capped so a long-lived failure still retries
        // on a sane cadence.
        var backoff = TimeSpan.FromTicks(Math.Min(
            BaseBackoff.Ticks * (long)Math.Pow(2, Math.Min(message.AttemptCount - 1, 6)),
            TimeSpan.FromHours(1).Ticks));
        if (!await TryResolveAsync(dbContext, message, "failed", cancellationToken, m => m.MarkFailed(now, reasonCode, backoff)))
        {
            return;
        }

        if (message.IsPending)
        {
            logger.LogWarning(
                "Identity email {MessageId} failed on attempt {Attempt} of {MaxAttempts} and will be retried: {Reason}",
                message.MessageId,
                message.AttemptCount,
                message.MaxAttempts,
                reasonCode);
            return;
        }

        logger.LogError(
            "Identity email {MessageId} for account {AccountId} was abandoned after {Attempt} attempts: {Reason}",
            message.MessageId,
            message.AccountId,
            message.AttemptCount,
            reasonCode);
    }

    /// <summary>One due message this pass intends to try. Carries identifiers only - no decision.</summary>
    private sealed record OutboxCandidate(string MessageId, string ChallengeId);

    /// <summary>A committed claim on one message, and the instant that claim expires.</summary>
    private sealed record OutboxClaim(IdentityEmailOutboxMessage Message, DateTimeOffset ExpiresAt);
}

/// <summary>
/// Wakes the dispatcher as soon as a transaction that staged a message commits, instead of leaving
/// it to the next idle pass. Signalling is best-effort: a dropped signal delays a message by one
/// interval and never loses it, because the idle pass finds the same durable rows.
/// </summary>
internal sealed class IdentityEmailOutboxSignal : IIdentityEmailDispatchTrigger, IDisposable
{
    private readonly SemaphoreSlim gate = new(0, 1);

    public void RequestDispatch()
    {
        try
        {
            gate.Release();
        }
        catch (SemaphoreFullException)
        {
            // A pass is already pending; one wake-up covers every message it will find.
        }
        catch (ObjectDisposedException)
        {
            // The host is shutting down.
        }
    }

    internal async Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            await gate.WaitAsync(timeout, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Shutdown: the caller's loop observes the token.
        }
    }

    public void Dispose() => gate.Dispose();
}
