# IdentityAuth rate limiting and abuse protection

Status: `PLAT-SEC-01` implemented application boundary.

This authority applies only to the externally reachable IdentityAuth operations below. It does not
create a Platform-wide rate-limiting policy.

| Operation | Route | Origin limit | Subject limit | Window |
|---|---|---:|---:|---:|
| `registerAccount` | `POST /auth/accounts` | 20 | 5 | 600 seconds |
| `requestEmailVerification` | `POST /auth/email-verification-requests` | 30 | 5 | 600 seconds |
| `verifyEmail` | `POST /auth/email-verifications` | 60 | 10 | 600 seconds |
| `signIn` | `POST /auth/sessions` | 60 | 10 | 300 seconds |
| `refreshSession` | `POST /auth/session/refresh` | 300 | 60 | 60 seconds |

Every request first consumes the operation-specific network-origin quota, before request metadata or
the JSON body is accepted. A contract-valid body then consumes an independent subject quota before
the application handler runs. Email operations use the trimmed, case-insensitive email identifier
for subject partitioning whether or not that account exists. Refresh uses the stable session
identifier carried by a structurally valid refresh credential; malformed credentials use their own
opaque keyed digest. This prevents rotation from resetting a valid session's subject quota while
leaving invalid credentials covered by both origin and credential partitions.

Partition identifiers are HMAC digests made with the existing externally configured IdentityAuth
pepper. Raw email addresses, network addresses, passwords, OTP codes, refresh credentials, and
partition digests are not written to the rejection log. Telemetry records only the operation,
limiting dimension, and retry delay.

A rejected request receives:

- HTTP `429 Too Many Requests`;
- the existing `application/problem+json` shape with `code = RATE_LIMITED`,
  `retryable = true`, and no account-state detail;
- `Retry-After` containing a positive whole-second delay;
- `Cache-Control: no-store` and the normal `X-Correlation-Id`.

The same quota selection and rejection shape apply to existing and nonexistent email subjects.
Rate-limit rejection happens before account lookup, password verification, OTP verification, or
session mutation. Existing password hashing/constant-work unknown-account handling, OTP challenge
attempt ceilings, refresh-token hashing and rotation, session validation, idempotency, and
anti-enumeration responses are unchanged.

## Configuration

Configuration is rooted at `IdentityAuth:AbuseProtection`. Each operation group has
`OriginPermitLimit`, `SubjectPermitLimit`, and `WindowSeconds`. Every value is required to be in
the range `1..100000` for permit limits and `1..86400` for windows; invalid values fail host
startup. There is no configuration switch that silently disables this application boundary.

## Horizontal deployment boundary

The application limiter deliberately owns process-local fixed-window state. It protects every
instance and guarantees the configured ceilings on that instance, but a process restart clears its
windows and multiple instances do not share counters. This is not represented as a cluster-wide
guarantee.

A production deployment with more than one ApiHost instance must therefore enforce equivalent or
stricter aggregate origin and subject quotas at a trusted ingress or through a distributed limiter.
The ingress must also preserve the real client origin through an explicitly trusted proxy
configuration. The application uses `HttpContext.Connection.RemoteIpAddress` and does not trust a
caller-supplied forwarding header by itself. Until those deployment controls exist, the configured
application limits are per instance and the effective cluster allowance can scale with instance
count.

## Runtime verification

On 2026-09-04 an isolated LocalDB/ApiHost run passed 136 focused checks covering all five operations,
normal registration/sign-in/refresh, origin and subject exhaustion, uniform known/unknown-account
throttle shapes, email-case normalization, refresh rotation, malformed/invalid input, retry after
the advertised window, forwarding-header spoof resistance, secret-free rejection output/logging,
and fail-fast invalid configuration. The existing full OTP/outbox regression and IdentityAuth read
audit suites also passed. This evidence is scoped to IdentityAuth and does not establish
Platform-wide security completion.
