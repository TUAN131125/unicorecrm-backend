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
