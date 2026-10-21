# Backend development runtime

Normal ApiHost startup is runtime-only:

```powershell
dotnet run --project src/UnicoreCRM.ApiHost
```

It does not apply migrations or seed demo IdentityAuth, Workspace, AccessControl, or Integration
state. The configured database must already have the required owner migrations.

Apply every owner-registered EF Core migration explicitly, then exit:

```powershell
dotnet run --project src/UnicoreCRM.ApiHost -- --migrate
```

The owner modules retain their own `DbContext` and migration assembly ownership. ApiHost only
orders the registered owner callbacks. The AccessControl legacy normalization correction is part
of this explicit maintenance pass and no longer runs when the HTTP application starts.

The optional demo fixture is also an exit-after-completion command and is available only in the
Development environment:

```powershell
dotnet run --project src/UnicoreCRM.ApiHost -- --seed-demo
```

The demo command expects the schema to be current. For a new local database, run `--migrate`
first, or pass both explicit commands in one invocation:

```powershell
dotnet run --project src/UnicoreCRM.ApiHost -- --migrate --seed-demo
```

Copy `src/UnicoreCRM.ApiHost/appsettings.Development.Local.example.json` to the ignored
`appsettings.Development.Local.json` only when local overrides or a demo password are wanted.
Development uses the local logging email sender by default, so starting the host does not require
Gmail or any other external mail transport. Configure `GmailSmtp` locally only when real delivery
is intentionally being exercised.


## Required local secrets

The Development ApiHost uses the built-in .NET User Secrets provider with ID
`unicorecrm-apihost-development`. Configure these keys outside Git before starting:

- `ConnectionStrings:UnicoreCRM`
- `IdentityAuth:Jwt:SigningKey` (at least 32 characters)
- `IdentityAuth:RefreshTokenPepper` (at least 32 characters)

Use `dotnet user-secrets` from the ApiHost project, preferably feeding a JSON object
through stdin to avoid command-line secret exposure. Never paste values into source,
committed examples, logs or shell history. User Secrets is a local plaintext store;
protect access to the operating-system account. It is not a production secret vault.

Precedence in Development is tracked defaults, ignored local JSON, User Secrets,
environment variables, then command-line settings. The optional ignored local JSON
continues to support non-secret overrides. Do not enable real mail or Telegram merely
to verify configuration. Normal startup does not migrate or seed the database.

Published development signing keys or peppers must be treated as compromised.
Removing them from the current tree does not revoke historical copies or active tokens.
Rotation requires an explicit, coordinated cutover and session/email workflow review.
