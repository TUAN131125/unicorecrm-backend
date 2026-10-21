# Lead Table cursor regression

This bounded verifier covers `LC-CA-001`. It does not establish production capacity,
whole-system authorization security, or shared production DataProtection topology.

Focused crypto/handler tests (no database):

```powershell
dotnet run --project scripts/LeadTableCursorVerifier/LeadTableCursorVerifier.csproj
```

Connected proof requires Windows integrated SQL access to a local Developer edition
instance, `sqlcmd`, the built official ApiHost, and the existing frontend Node/Vite/
Playwright dependencies. From the backend repository:

```powershell
dotnet build src/UnicoreCRM.ApiHost/UnicoreCRM.ApiHost.csproj
python scripts/LeadTableCursorVerifier/public_http_sql.py --output <new-absolute-evidence-directory> --frontend <absolute-frontend-repository>
```

The output directory must not exist. A UUID database is created with a nonce ownership
marker and manifest, using SQL Server-managed default data/log directories. RAM, disk,
and active-request guards are retained. Cleanup verifies server identity, marker,
managed file paths and zero active database sessions before SQL-managed DROP; unrelated
database metadata must match the initial snapshot. No physical files are deleted.
Only this fixture receives migrations and a controlled timestamp tie fixture.

Official maintenance bootstrap creates real accounts/memberships/roles. All scenario
authentication, authorization changes and aggregate creation use public APIs with real
sessions and normal middleware. No synthetic evaluator is installed in the ApiHost.
The browser signs in through the actual connected frontend; API responses are not
intercepted. Its error proof changes a real role policy, observes cursor rejection,
then explicitly resets the search and refreshes.

Output includes secret-redacted logs, scenario results and cleanup evidence. Generated
passwords, signing keys, tokens and the isolated private key ring are never committed.
The harness loads tracked base appsettings into its private content root and never
loads or edits local `appsettings.Development.json`. It exits nonzero for failed or
blocked scenarios, setup failures, missing scenarios, or unconfirmed cleanup.

Focused assertions use an ephemeral protector and recording persistence, separately
from the connected proof, to verify invalid tokens reach neither List nor Count.
These test doubles are not production security evidence by themselves.
