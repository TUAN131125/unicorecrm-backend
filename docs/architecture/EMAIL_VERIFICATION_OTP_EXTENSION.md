# Email Verification OTP Extension

## Authority

`PROJECT_EXTENSION_EMAIL_VERIFICATION_OTP`

`CURRENT_IMPLEMENTATION_AUTHORITY.md` recorded `verifyEmail` as a fail-closed `AUTHORITY_GAP`:
the adopted OpenAPI declares `POST /auth/email-verifications` with a `VerifyEmailRequest` carrying
an opaque `token`, but no current authority defined verification-code issuance, delivery, hashing,
expiry, attempt limits or consumption. B01 therefore implemented registration into
`PENDING_VERIFICATION` with no admitted path out of it.

This extension resolves that gap under explicit project authority. The admitted business decision is
that a **six-digit one-time code delivered by email is the canonical email-verification credential**.
It supersedes the token-based request shape.

The extension is owned entirely by `platform/identity-auth`. It changes no Workspace or
AccessControl ownership, adds no workspace provisioning behaviour to registration, and unblocks
nothing else: `verifyMfa`, `requestPasswordReset`, `resetPassword`, `acceptWorkspaceInvitation` and
every other operation listed as fail-closed remain exactly as recorded.

| Operation | Contract | Classification |
|---|---|---|
| `registerAccount` | `POST /auth/accounts` | unchanged wire contract; now also issues the first challenge |
| `requestEmailVerification` | `POST /auth/email-verification-requests` | `PROJECT_EXTENSION_EMAIL_VERIFICATION_OTP` |
| `verifyEmail` | `POST /auth/email-verifications` | canonical path; **request body superseded by this extension** |
| `signIn`, `getCurrentSession`, `refreshSession`, `signOut` | unchanged B01 contracts | unchanged |
| `verifyMfa`, `requestPasswordReset`, `resetPassword`, `acceptWorkspaceInvitation` | — | still `AUTHORITY_GAP`; not implemented |

## Retired contract

The token-based `VerifyEmailRequest` is **retired**. It is not implemented, has no route shape and
must not be reintroduced:

```json
{ "token": "<opaque 16-4096 character token>" }
```

A request carrying `token` is rejected with `VALIDATION_FAILED`, because the host rejects unmapped
request members. There is no verification link, no emailed URL and no token issuance anywhere in the
implementation.

## Contract file precedence

This document is the authoritative HTTP contract for these two operations.

`frontend/unicorecrm-web/docs/api/openapi.json` is the adopted current OpenAPI authority and is
read-only to backend work; `design-authority/contracts/openapi.json` is its byte-identical
historical baseline. Both remain at the verified SHA-256
`8278547df0fd4be9a9af9b8a6d5f3e15ddad8d005d804c99a7c9248e0f402757`, and neither is edited here, for
the same reason `PROJECT_EXTENSION_INBOUND_LEAD_WEBHOOK`, `PROJECT_EXTENSION_AI_ASSISTANT` and
`PROJECT_EXTENSION_INITIAL_WORKSPACE_PROVISIONING` did not edit them: the pinned artefact is a
verified baseline that many operation admission rows reference by hash, and a backend-local decision
does not rewrite it. Where this document and the pinned OpenAPI disagree about
`POST /auth/email-verifications`, **this document controls the implemented backend**, and the
divergence is deliberate and recorded rather than accidental.

## Lifecycle

```text
POST /auth/accounts
  -> account created with status PENDING_VERIFICATION, emailVerifiedAt = null
  -> one verification challenge and one outbox message committed together
  -> the dispatcher delivers the code after the commit
POST /auth/sessions before verification
  -> 403 EMAIL_NOT_VERIFIED
POST /auth/email-verification-requests   (optional, repeatable)
  -> supersedes the previous usable code and dispatches a new one
POST /auth/email-verifications with the current code
  -> account status ACTIVE, emailVerifiedAt = now, challenge consumed
POST /auth/sessions
  -> 200 authenticated session
```

Registration still provisions no Workspace, and workspace onboarding continues to run exactly as
`PROJECT_EXTENSION_INITIAL_WORKSPACE_PROVISIONING` describes, after sign-in.

## Common request rules

Both operations are anonymous and use the existing IdentityAuth header contract:

- `X-Request-Id`: required, 8–128 characters;
- `Idempotency-Key`: required, 8–128 characters;
- `X-Correlation-Id`: optional, 8–128 characters; otherwise the server trace identifier is used, and
  the resolved value is echoed in the `X-Correlation-Id` response header.

Unknown request members are rejected. Failures use the canonical `application/problem+json`
`ProblemDetails` shape.

## `POST /auth/email-verification-requests`

Operation name `requestEmailVerification`.

### Request

```json
{ "email": "user@example.com" }
```

`email` is required, must be a valid address and must not exceed 254 characters.

### Response `202`

```json
{
  "requestId": "evr_2f1c1c2c9b0a4a1e8f1c4d6a7b8c9d0e",
  "acceptedAt": "2026-08-25T01:38:15.123Z"
}
```

`requestId` is a server-assigned acceptance identifier. It carries no account identity, is not an
account ID, is not a verification credential and cannot be submitted anywhere.

### Errors

| Status | Code | Cause |
|---|---|---|
| 422 | `VALIDATION_FAILED` | missing/invalid header or body |
| 409 | `IDEMPOTENCY_KEY_REUSED` | the key was already used with a different address |
| 503 | `INTEGRATION_UNAVAILABLE` | this host has no usable email sender configured. A *transient* provider failure does not appear here: the message is queued and retried |
| 500 | `INTERNAL_ERROR` | unexpected server failure |

## `POST /auth/email-verifications`

Operation name `verifyEmail`.

### Request

```json
{
  "email": "user@example.com",
  "code": "123456"
}
```

`code` must be exactly six digits (`^[0-9]{6}$`).

### Response `200`

The canonical `UserAccountDocument`, with `status = "ACTIVE"` and `emailVerifiedAt` set:

```json
{
  "accountId": "acc_863780d5fe8b4842baaede3c3d406786",
  "email": "user@example.com",
  "displayName": "Example User",
  "status": "ACTIVE",
  "createdAt": "2026-08-25T01:38:15.101Z",
  "emailVerifiedAt": "2026-08-25T01:41:02.774Z"
}
```

### Errors

| Status | Code | Cause |
|---|---|---|
| 422 | `VALIDATION_FAILED` | missing/invalid header or body, or a code that is not exactly six digits |
| 401 | `TOKEN_INVALID` | unknown address, an account not awaiting verification, no outstanding challenge, a superseded or consumed challenge, or a wrong code |
| 401 | `TOKEN_EXPIRED` | the outstanding code has expired |
| 429 | `RATE_LIMITED` | the attempt ceiling for the outstanding code is spent |
| 409 | `IDEMPOTENCY_KEY_REUSED` | the key was already used with a different address or code |
| 500 | `INTERNAL_ERROR` | unexpected server failure |

## Security rules

1. **The plaintext code is never persisted.** The challenge stores only a keyed HMAC-SHA256 digest,
   bound to the owning account so a digest read from one row cannot be replayed against another. The
   HMAC key is derived from the configured identity pepper under a distinct purpose label, so the
   same secret never yields interchangeable digests across refresh tokens, idempotency fingerprints
   and verification codes. Comparison is fixed-time.
2. **Codes are unpredictable.** Six digits drawn from a cryptographic generator across the whole
   `000000`–`999999` range without modulo bias.
3. **Codes expire.** The lifetime is server-owned configuration clamped to the admitted 5–10 minute
   window. An expired code cannot be consumed, and the expiry decision is taken from persisted state
   rather than from anything the caller sends.
4. **Attempts are capped.** Each challenge carries its own `AttemptCount`/`MaxAttempts`. A wrong code
   commits its increment before the response is written, so the ceiling survives a caller that simply
   retries. Once the ceiling is reached, even the correct code is refused with `RATE_LIMITED` and the
   caller must request a new one.
5. **Codes are single-use.** Consumption is recorded on the challenge inside the same serializable
   transaction that activates the account, so a consumed code can never verify again.
6. **A resend supersedes.** Issuing a new challenge marks every outstanding challenge for the account
   superseded, so at most one code is ever usable and the previous one stops working immediately.
7. **Resends are throttled without disclosure.** A request inside the cooldown is accepted with the
   same `202` shape and simply issues nothing; the previously issued code stays usable. The cooldown
   is never reported as a distinguishable rejection. The cooldown is measured against the most recent
   outstanding challenge, not against the most recent *usable* one, so spending the attempt ceiling
   does not buy a fresh code and the ceiling cannot be converted into an unthrottled guessing loop.
8. **Existence is not disclosed.** A contract-valid request returns the same `202` shape for an
   unknown address, an already active account, a suspended account and an account inside its
   cooldown. Verification failures collapse to one `TOKEN_INVALID` answer. The two states reported
   distinctly — expired and attempts exhausted — are reported because the caller must be able to act
   on them, and both require the caller to already hold an outstanding challenge.
9. **Only a pending account is activated.** `MarkEmailVerified` refuses any status other than
   `PENDING_VERIFICATION`, so a suspended account is never reinstated by email verification and an
   active account is never re-stamped.
10. **The queued code is encrypted, never stored in the clear.** Delivery happens after the issuing
    transaction commits, so the outbox must be able to reconstruct the one code it still has to send.
    It holds AES-GCM ciphertext under a purpose-separated key derived from the identity pepper, with
    the owning challenge identifier as associated data, so a payload cannot be moved to another row
    and cannot be read without the host's configured secret. Verification never uses this path - it
    compares against the one-way digest on the challenge - and the ciphertext is cleared the moment
    the message is sent or abandoned.
11. **Every issuance, failure and success is audited.** `iam.AuditRecords` records the operation
    outcome and `iam.SecurityEvents` records `IDENTITY_EMAIL_VERIFICATION_ISSUED`,
    `IDENTITY_EMAIL_VERIFICATION_FAILED` and `IDENTITY_EMAIL_VERIFIED`. Neither carries a code.

### Accepted residual risks

- A six-digit code has a small keyspace by contract. The digest is a containment measure against a
  leaked database row, not the primary control; short expiry, the attempt ceiling, single use and
  supersession carry that weight.
- An unknown address is answered before any digest is computed, so a precise attacker could in
  principle time the difference. The gap is dominated by the database round trips on both paths and
  is not compensated with a synthetic hash.
- The queued code is recoverable by anyone holding both the database and the host's configured
  pepper. That is strictly better than storing it in the clear, and an attacker with both already has
  more than a ten-minute single-use code; the ciphertext also exists only between issuance and
  delivery.
- Delivery is at-least-once, so a crash in a narrow window can send the same code twice. Sending the
  same code twice is harmless; the alternative, at-most-once, would silently lose codes.
- A configured-but-failing email boundary is reported as `INTEGRATION_UNAVAILABLE` for an address
  that has a pending account, which is observably different from the `202` an unknown address
  receives. Telling a caller a code is on its way when it is not was judged worse.

## Persistence

`iam.EmailVerificationChallenges`, owned by `IdentityAuthDbContext`:

| Column | Type | Notes |
|---|---|---|
| `ChallengeId` | `nvarchar(64)` PK | server-assigned, `evc_` prefix |
| `AccountId` | `nvarchar(64)` | FK to `iam.Accounts`, cascade delete |
| `CodeHash` | `nvarchar(64)` | hex HMAC-SHA256 digest; never the code |
| `CreatedAt` | `datetimeoffset` | |
| `ExpiresAt` | `datetimeoffset` | |
| `ResendAvailableAt` | `datetimeoffset` | |
| `AttemptCount` | `int` | |
| `MaxAttempts` | `int` | captured at issuance, so a configuration change never retroactively widens a live challenge |
| `ConsumedAt` | `datetimeoffset?` | single-use marker |
| `SupersededAt` | `datetimeoffset?` | resend supersession marker |

Index `(AccountId, ConsumedAt, SupersededAt)` serves the only query the owner performs: the
outstanding challenges of one account.

`iam.EmailOutboxMessages`, also owned by `IdentityAuthDbContext`:

| Column | Type | Notes |
|---|---|---|
| `MessageId` | `nvarchar(64)` PK | server-assigned, `eom_` prefix |
| `AccountId` | `nvarchar(64)` | FK to `iam.Accounts`, cascade delete |
| `ChallengeId` | `nvarchar(64)` | **unique**: one message per challenge |
| `Recipient` / `DisplayName` | `nvarchar(254)` / `nvarchar(160)` | addressing captured at issuance |
| `ProtectedCode` | `nvarchar(512)?` | AES-GCM ciphertext of the code; never the code; cleared on a terminal state |
| `CodeExpiresAt` | `datetimeoffset` | a message whose code expired before delivery is cancelled rather than sent |
| `Status` | `nvarchar(32)` | `Pending`, `Sent`, `Abandoned` or `Cancelled` |
| `AttemptCount` / `MaxAttempts` | `int` | delivery attempts; unrelated to verification attempts |
| `CreatedAt` / `NextAttemptAt` | `datetimeoffset` | when the message may next be claimed |
| `LeasedUntil` | `datetimeoffset?` | non-null only while a delivery attempt is claimed and unresolved |
| `RowVersion` | `rowversion` | optimistic concurrency token; a stale delivery outcome fails rather than overwriting |
| `LastAttemptAt` / `SentAt` | `datetimeoffset?` | |
| `LastError` | `nvarchar(500)?` | a bounded application-owned reason code, never provider text |

Index `(Status, NextAttemptAt)` serves the dispatcher's only query: the due pending messages, oldest
first. The unique `ChallengeId` index is the retry-safety invariant - a delivery retry can repeat an
email but can never produce a second challenge or a second account.

`Cancelled` is terminal and non-deliverable and means the challenge whose code the message carried
stopped being usable before that code was delivered. It is deliberately distinct from `Sent`,
because the email never left, and from `Abandoned`, which means delivery was attempted and failed.

`LeasedUntil` is the one durable signal separating "queued, waiting for its next attempt" from
"handed to the provider right now". The claim commits it before any network call, so any other
transaction can read it. `NextAttemptAt` alone cannot answer that question: it sits in the future
both while a claim is held and while a failed attempt waits out its backoff.

Migrations: `20260825013815_IdentityEmailVerification`, `20260825031648_IdentityEmailOutbox`,
`20260825060515_IdentityEmailOutboxSupersession` and
`20260825072836_IdentityEmailOutboxConcurrencyToken`, all under the IdentityAuth owner only and
all additive. No other owner's model, snapshot or migration chain is touched.

## Email sender boundary

`IIdentityEmailSender` is IdentityAuth-owned and provider-neutral, and it answers two separate
questions. `EnsureConfigured()` asks "could this host ever deliver mail?" with no network call, on
the request path, before anything is persisted. `SendEmailVerificationCodeAsync` performs the remote
call, and only the outbox dispatcher calls it.

Four senders exist, selected by `IdentityAuth:EmailVerification:Sender:Kind`:

- `Unavailable` — the default in **every** environment and the fallback for every unrecognised kind.
  `EnsureConfigured()` always throws, so registration and verification requests fail closed with
  `INTEGRATION_UNAVAILABLE` rather than creating accounts nobody can activate.
- `DevelopmentLog` — writes the code to the backend console and sends nothing. Registered only when
  the running host environment is Development **and** the kind is explicitly `DevelopmentLog`. A
  deployed host cannot reach it even if the configuration value is present.
- `DevelopmentFailing` — always fails, the way a hostile provider would, with an error string that
  echoes the recipient, the full subject and the live code back at the caller. It exists only so a
  verification harness can prove that provider-authored text reaches neither `LastError` nor a log.
  Gated exactly like `DevelopmentLog`. It writes the text it fabricates to
  `Sender:SimulatedFailureTranscriptPath`, which stands in for a provider's own transcript, so the
  harness asserts against the real values rather than trusting the sender.
- `GmailSmtp` — real delivery through Gmail's submission service, available to any environment.

`GmailSmtpIdentityEmailSender` is the only type in the solution that touches an SMTP or MIME type. It
uses MailKit, and no MailKit or MimeKit type appears in IdentityAuth's Domain, Application or
Contracts layer or in any other module. It authenticates with the configured `Username` and a Google
App Password, and the transport is always encrypted: STARTTLS on the submission port, or implicit TLS
when `UseStartTls` is off. There is deliberately no plaintext fallback. The message carries the
six-digit code, its expiry instant and UnicoreCRM branding, as both a plain-text and an HTML body.

`EnsureConfigured()` for `GmailSmtp` requires `Host`, a port in range, `Username`, `AppPassword`, a
parseable `FromAddress` and a sane `TimeoutSeconds`. A failure names the missing settings and never
their values.

### Delivery failures are classified, never quoted

The sender logs nothing at all, and no provider-authored text leaves it. A failed send is mapped onto
one of IdentityAuth's own bounded classifications and the provider exception is discarded:
`SMTP_AUTH_FAILED`, `SMTP_CONNECT_FAILED`, `SMTP_TIMEOUT`, `SMTP_PROTOCOL_ERROR`,
`SMTP_COMMAND_FAILED`, `SMTP_RECIPIENT_REJECTED`, `SMTP_PROVIDER_UNAVAILABLE` or
`UNKNOWN_DELIVERY_FAILURE`. Only the exception's *type* is inspected; its message is never read.

This is a change of kind, not of degree. Redacting the username and app password out of a provider
message is a denylist, and a denylist cannot cover text the provider composes. SMTP error text quotes
server dialogue, and that dialogue routinely echoes the envelope back - the recipient address, the
headers, and for this product a `Subject` line that **contains the verification code itself**. The
complete vocabulary that may ever be written to `iam.EmailOutboxMessages.LastError` is the set of
constants in `EmailOutboxReasons`; the dispatcher adds `EMAIL_SENDER_UNAVAILABLE`,
`PAYLOAD_UNREADABLE`, `CODE_EXPIRED_BEFORE_DELIVERY`, `CHALLENGE_SUPERSEDED`, `CHALLENGE_CONSUMED`,
`CHALLENGE_EXPIRED` and `CHALLENGE_NOT_DELIVERABLE` for the outcomes it decides itself. Logs carry
the same codes plus identifiers and attempt counts - never a recipient, a code, a credential or
provider text.

## Delivery: the IdentityAuth email outbox

Remote SMTP must not run inside the serializable transaction that issues a challenge: a network call
holds locks for its whole duration, and a provider outage would otherwise roll back an account.

Issuance therefore performs no I/O. It calls `EnsureConfigured()` first, then stages the challenge and
exactly one `iam.EmailOutboxMessages` row in the same transaction, and commits. `IdentityEmailOutboxDispatcher`,
an IdentityAuth-owned hosted service, delivers afterwards. A committed transaction signals the
dispatcher so it runs immediately; the signal is best-effort, and a dropped signal delays a message by
one idle interval rather than losing it, because the idle pass finds the same durable rows.

PlatformOperations owns an `Outbox` module, but it is an empty placeholder with no approved
cross-owner contract, and LAW-04/LAW-05 forbid reaching into another owner's persistence. This is
therefore the smallest IdentityAuth-owned durable mechanism: one table and one hosted service. No
broker, queue product, scheduler or other distributed infrastructure is introduced.

### Retry semantics

Claiming is a lease, and **a claim covers exactly one message**.

A pass first reads a bounded set of due candidates - identifiers only, no locks, no claim. That list
decides only what the pass will *attempt*. Each candidate is then re-read, re-checked and claimed in
its own small serializable transaction **immediately before its own send**: the transaction confirms
the row is still pending, still due and not already claimed, re-checks the challenge, counts the
attempt, stamps `LeasedUntil` from the current clock, and **commits before any network call**.

Claiming the whole batch at once cannot express that. Messages are delivered sequentially, so a
single `LeasedUntil` shared by twenty of them is already stale by the time the later ones start: with
a 120-second lease, a 20-message batch and 30-second sends, message five can begin sending after the
claim covering it has expired. A send running under a lapsed claim is precisely the state a resend is
entitled to assume cannot exist, because the resend would then revoke a code already on its way. Each
message therefore gets its own claim, measured from the moment that claim commits.

A dispatcher that dies mid-send releases its message when that message's own lease expires rather
than stranding it, and two passes never send the same message at once, because a candidate that is
already claimed is skipped. Because the stamp is committed before the send, it is also how another
transaction learns that a code is already on its way; see *The in-flight race* below.

Delivery is at-least-once: a crash between a successful send and its outcome write can repeat an
email. A repeat can only ever resend the *same* code, because the message is keyed one-to-one to its
challenge by a unique index and creates no account or challenge state of its own. A transient
provider failure therefore never produces a duplicate account or a duplicate OTP challenge.

A failed attempt records a bounded reason code and reschedules with exponential backoff from the
configured base, capped at one hour. Once `MaxAttempts` is reached the message is abandoned and its
payload cleared; the account simply stays `PENDING_VERIFICATION` and the holder can request a new
code. A message whose code expired before it could be delivered is cancelled rather than sent. If the
host's sender configuration is unusable, the pass holds every message untouched instead of burning
attempts against a problem no retry can fix.

Because eligibility is now re-checked *inside* the claim transaction and *before* the attempt is
counted, a message that is retired without ever being sent no longer spends one of its delivery
attempts. That is a side effect of per-message claiming rather than a separate change.

### A revoked code is never delivered

A queued message carries a credential, so it is deliverable only for as long as its challenge is.
The moment a challenge is superseded, consumed or expired, its code is revoked, and delivering a
revoked code is worse than delivering nothing: the holder would enter it, fail, and spend an attempt
of the challenge that actually *is* active.

Two mechanisms enforce this, and they are deliberately redundant.

1. **The transaction that revokes the code retires the message.** Issuing supersedes every
   outstanding challenge; in the same serializable transaction it cancels those challenges'
   still-undelivered messages, setting `Status = Cancelled`, dropping `ProtectedCode` and recording
   `CHALLENGE_SUPERSEDED`. Verification does the same with `CHALLENGE_CONSUMED` for the challenges it
   closes.
2. **The dispatcher fails closed before any network I/O.** Immediately before each send it re-reads
   the challenge and cancels the message without calling the provider if it has been superseded,
   consumed or expired. This covers the states no issuing transaction wrote - an expiry that simply
   elapsed - and any future writer that forgets rule 1.

### The in-flight race

A code that is *already being delivered* cannot be revoked, because the send may already have reached
the provider. Cancelling it would produce exactly the state the outbox must never reach: old code
invalid, new code active, old email still arriving.

So a resend does not force the issue. It reads the account's undelivered messages inside its own
serializable transaction and, if any is still claimed, raises `IdentityEmailDeliveryInFlightException`
instead of issuing. `requestEmailVerification` answers with its usual uniform `202` and an
`ACCEPTED_DELIVERY_IN_FLIGHT` audit outcome, issues nothing, and does not restart the cooldown, so the
caller may simply ask again. Registration cannot reach this path: a new account has no outstanding
challenge.

The two transactions serialize because both write the same outbox row under `SERIALIZABLE`, and the
claim commits before the network call. Either the claim commits first and the resend sees it and
declines, or the cancellation commits first and the message is no longer claimable. There is no
interleaving that revokes a code while its email is still on its way, and no SMTP call has moved into
the issuing transaction to achieve it.

The claim is what bounds the window, and two rules keep the send inside it. The effective lease is
never shorter than `senderTimeout + 30s`, so a claim always outlasts the send it protects. And every
send is capped at the *remaining* time on the claim it was granted under: the deadline is computed
from that claim's committed expiry, and if it has somehow already passed the sender is **not called
at all** - the claim is released and a later pass claims the message afresh. There is deliberately no
path that falls back to the sender's own timeout once the durable claim has lapsed, because that
timeout is not the thing a resend reads.

### The concurrency token

`RowVersion` is defence in depth behind the claiming rules, not a substitute for them.

A delivery attempt reads its row, spends seconds talking to a provider, and only then writes the
outcome. In that window a resend or a verification may legitimately retire the same row. The token
makes the collision loud: an outcome written against a stale row image raises
`DbUpdateConcurrencyException` instead of silently winning. The dispatcher then reloads the row,
leaves whatever the other writer committed exactly as it stands, and logs the conflict with the
status that was preserved. It never retries the write - a retry would be a finished send stamping
`Sent` over a `Cancelled` row, which is the database asserting that a revoked code was delivered.

### Consequence for registration

Registration fails closed only on *misconfiguration*. A host that cannot deliver mail at all still
returns `INTEGRATION_UNAVAILABLE` and creates nothing. But a **transient** provider failure - Gmail
unreachable, rejecting, or timing out - no longer fails registration: the account is created
`PENDING_VERIFICATION`, the message stays queued, and delivery is retried. This is the deliberate
trade for keeping SMTP out of the transaction, and it is why the outbox is durable rather than
in-memory.

## Configuration

```json
"IdentityAuth": {
  "EmailVerification": {
    "ExpiryMinutes": 10,
    "MaxAttempts": 5,
    "ResendIntervalSeconds": 60,
    "Sender": {
      "Kind": "Unavailable",
      "Host": "smtp.gmail.com",
      "Port": 587,
      "UseStartTls": true,
      "FromName": "UnicoreCRM",
      "TimeoutSeconds": 30
    },
    "Outbox": {
      "DispatchIntervalSeconds": 15,
      "MaxAttempts": 5,
      "RetryBackoffSeconds": 30,
      "BatchSize": 20,
      "LeaseSeconds": 120
    }
  }
}
```

`Username`, `AppPassword` and `FromAddress` are deliberately **absent** from every tracked file. They
are secrets or personal data and belong in the untracked
`appsettings.Development.Local.json` locally, or in the deployment's own secret store.

`Sender:SimulatedFailureTranscriptPath` is empty in every tracked file and is read only by the
Development-only `DevelopmentFailing` sender. `Sender:SimulatedSendDelayMilliseconds` is zero in every
tracked file and is read only by the Development-only `DevelopmentLog` sender, where it holds a
delivery attempt open so a harness can observe the claim protecting it; it honours the caller's
cancellation token, so a claim deadline still cuts the send short.

`ExpiryMinutes` is clamped to 5–10, `MaxAttempts` to 1–10 and `ResendIntervalSeconds` to 30–3600 in
code, as are the outbox values, because nested option members are not covered by the host's
data-annotation validation. The shipped `appsettings.json` default is the fail-closed sender;
`appsettings.Development.json` selects `DevelopmentLog`.

## Out of scope

This extension adds no verification link or emailed URL, no password-reset flow, no MFA, no email
template system beyond the one verification message, no provider other than SMTP, no bounce or
complaint handling, no admin verification override, no background expiry sweep of spent challenges or
of terminal outbox rows, and no frontend change. The current frontend `VerifyEmailPage`
still reads a `token` query parameter and calls the retired contract; aligning it to the OTP contract
is separate frontend work.
