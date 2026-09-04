# Platform CI Quality Gate

Status: `IMPLEMENTED`

Authority identifier: `PLAT-QA-01-platform-required-gate`

This document defines the repository-owned blocking verification gate for Platform changes. It does
not freeze Platform and does not assert that repository branch protection is configured.

## Verified subject and result integrity

`.github/workflows/platform-ci.yml` runs from an Actions checkout of `${{ github.sha }}` on the
explicit `windows-2025` image. `scripts/verify-platform-ci.ps1` independently resolves `HEAD`,
requires it to equal `GITHUB_SHA`, and requires a clean working tree. A mismatch, dirty tree,
missing prerequisite, failed check, or check that could not run makes the required gate fail.

Every required verifier runs in a separate Windows PowerShell process and receives an isolated
database name derived from the verified commit and workflow run. The orchestrator records
`PASS`, `FAIL`, or `NOT_RUN` for every declared check and exits non-zero unless every required check
is `PASS`. It writes the commit, branch/detached state, gate/workflow hashes, verifier hashes,
commands, durations, database names, per-check logs, and result statuses to
`artifacts/platform-ci/platform-ci-evidence.json`. The workflow publishes that directory as an
artifact named with the exact commit SHA.

`-AllowDirtyWorkingTree` is available only for verification while authoring a local candidate. Such
evidence is explicitly labelled `WORKING_TREE`; it is not commit-bound CI evidence and must not be
used as proof that a commit passed.

## Required checks

The following checks are blocking. None uses `continue-on-error`.

| Required check | Executable evidence | Coverage |
| --- | --- | --- |
| SQL LocalDB start and connectivity | `sqllocaldb`, `sqlcmd` | Declared database prerequisite |
| Local .NET tool restore | `dotnet tool restore` | Repository-pinned EF Core migration CLI |
| Solution restore | `dotnet restore UnicoreCRM.slnx` | Dependency restoration from a clean checkout |
| Solution build | `dotnet build UnicoreCRM.slnx --configuration Debug --no-restore` | All solution projects, warnings as errors |
| IdentityAuth core and abuse protection | `verify-identity-auth-abuse-protection.ps1` | Registration, password sign-in, OTP request/submission throttling, rotating refresh, safe failures and anti-enumeration behavior |
| Email verification / OTP | `verify-email-verification-otp.ps1` | OTP lifecycle, attempts, delivery/outbox safety, sessions and related schema |
| Identity session read/audit | `verify-identity-read-audit.ps1` | Current-session behavior, Identity-owned read audit and pending-model check |
| Workspace list read/audit | `verify-list-my-workspaces-read-audit.ps1` | Authenticated Workspace listing, isolation, audit and pending-model check |
| Workspace bootstrap trust/read audit | `verify-get-workspace-bootstrap-read-audit.ps1` | Trusted Workspace bootstrap, concealment, audit floor and pending-model check |
| Initial Workspace provisioning | `verify-initial-workspace-provisioning.ps1` | New-account provisioning, replay/concurrency/recovery and usable initial access |
| Initial Workspace provisioning upgrade | `verify-initial-workspace-provisioning-upgrade.ps1` | Fresh and historical migration chains plus durable provisioning recovery |
| AccessControl record access | `verify-access-control-record-access.ps1` | Capability, workspace isolation, record scope, field security, audits and relevant pending-model checks |
| Create access role | `verify-create-access-role.ps1` | Role creation contract, authorization, idempotency, persistence and governance |
| Replace access role | `verify-replace-access-role.ps1` | Optimistic concurrency, policy replacement, idempotency and governance |
| Archive access role | `verify-archive-access-role.ps1` | Lifecycle transition, concurrency, idempotency and governance |
| Replace member access | `verify-replace-workspace-member-access.ps1` | Membership-role assignment, authorization, concurrency, idempotency and governance |
| Workspace access directory | `verify-get-workspace-access-directory.ps1` | Directory composition, isolation, concealment, read audit and schema |
| Administrative request-body bounds | `verify-access-control-administrative-body-limits.ps1` | Allowed, malformed, oversized, unauthorized and multibyte mutation payloads |
| Access-role Unicode migration/upgrade | `verify-create-access-role-unicode-upgrade.ps1` | Fresh schema, historical normalization upgrade and collision fail-closed behavior |

The database-dependent verifiers apply the real EF migration chains to isolated SQL Server LocalDB
databases. Pending-model checks embedded in the IdentityAuth, Workspace and record-access suites,
plus the provisioning and role-upgrade suites, are required schema/migration evidence. A script
that cannot execute is `NOT_RUN`, which fails the job; it is never reported as `PASS`.

## CI environment and local reproduction

The required runner dependencies are Windows PowerShell 5.1, the .NET 10 SDK, SQL Server LocalDB
instance `MSSQLLocalDB`, `sqlcmd`, and access to the declared NuGet sources. The EF Core CLI is pinned
to `10.0.11` in `.config/dotnet-tools.json` and restored by the gate. The workflow intentionally names
`windows-2025` rather than following the moving `windows-latest` label.

From a clean checkout with those dependencies installed:

```powershell
powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass `
  -File scripts/verify-platform-ci.ps1 `
  -EvidenceDirectory artifacts/platform-ci
```

## Explicitly excluded checks

The following are not PR-blocking and are not represented as passing by this gate:

- `verify-gmail-transport.ps1`: live provider conformance requires externally governed Gmail
  credentials and network/provider availability. It remains separate external conformance evidence.
- `verify-development-database.ps1` and `verify-development-local-configuration.ps1`: these verify
  operator-specific development connectivity and credential overlays, not the isolated CI subject.
- Business-module verification outside the Platform-owned dependencies exercised by the required
  record-access suite: broad business release gating is outside `PLAT-QA-01`.

## Repository governance prerequisite

The workflow creates the status check `Platform CI / Required Platform verification`, but a tracked
file cannot enable branch protection or make that check required. Repository administrators must
configure the protected integration branches to require that exact status check. Until that setting
is applied and verified in repository settings, the gate exists and fails correctly when run, but
merge enforcement remains an external repository-governance prerequisite.
