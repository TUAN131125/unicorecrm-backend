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
  -> one verification challenge persisted and one code dispatched
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
| 503 | `INTEGRATION_UNAVAILABLE` | no email sender is configured, or dispatch failed |
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
10. **Every issuance, failure and success is audited.** `iam.AuditRecords` records the operation
    outcome and `iam.SecurityEvents` records `IDENTITY_EMAIL_VERIFICATION_ISSUED`,
    `IDENTITY_EMAIL_VERIFICATION_FAILED` and `IDENTITY_EMAIL_VERIFIED`. Neither carries a code.

### Accepted residual risks

- A six-digit code has a small keyspace by contract. The digest is a containment measure against a
  leaked database row, not the primary control; short expiry, the attempt ceiling, single use and
  supersession carry that weight.
- An unknown address is answered before any digest is computed, so a precise attacker could in
  principle time the difference. The gap is dominated by the database round trips on both paths and
  is not compensated with a synthetic hash.
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

Migration: `20260825013815_IdentityEmailVerification`, under the IdentityAuth owner only. No other
owner's model, snapshot or migration chain is touched.

## Email sender boundary

`IIdentityEmailSender` is IdentityAuth-owned and provider-neutral. Two implementations exist:

- `UnavailableIdentityEmailSender` — the default in **every** environment. It throws, so the caller's
  transaction cannot commit. This is the only sender any non-Development host can resolve.
- `DevelopmentLoggingIdentityEmailSender` — writes the code to the backend console. It is registered
  only when the running host environment is Development **and**
  `IdentityAuth:EmailVerification:Sender:Kind` is explicitly `DevelopmentLog`. A deployed host cannot
  reach it even if the configuration value is present.

There is deliberately no no-op or "pretend success" production sender. Until a real provider is
implemented and configured, registration and verification requests fail closed with
`INTEGRATION_UNAVAILABLE` rather than creating accounts nobody can activate.

Dispatch happens inside the caller's serializable transaction, after the challenge is saved and
before commit, so the persisted challenge and the dispatched code are all-or-nothing. This is
acceptable for a local console sender; a real remote provider should move dispatch behind an
IdentityAuth-owned outbox rather than widening this boundary, because a network call inside a
serializable transaction holds locks for the duration of the call.

## Configuration

```json
"IdentityAuth": {
  "EmailVerification": {
    "ExpiryMinutes": 10,
    "MaxAttempts": 5,
    "ResendIntervalSeconds": 60,
    "Sender": { "Kind": "Unavailable" }
  }
}
```

`ExpiryMinutes` is clamped to 5–10, `MaxAttempts` to 1–10 and `ResendIntervalSeconds` to 30–3600 in
code, because nested option members are not covered by the host's data-annotation validation. The
shipped `appsettings.json` default is the fail-closed sender; `appsettings.Development.json` selects
`DevelopmentLog`.

## Out of scope

This extension adds no verification link or emailed URL, no password-reset flow, no MFA, no email
template system, no outbound provider integration, no admin verification override, no background
expiry sweep of spent challenges, and no frontend change. The current frontend `VerifyEmailPage`
still reads a `token` query parameter and calls the retired contract; aligning it to the OTP contract
is separate frontend work.
