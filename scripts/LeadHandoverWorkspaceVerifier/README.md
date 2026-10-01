# Lead Handover Workspace verifier

Run from the backend directory:

```powershell
dotnet run --project scripts/LeadHandoverWorkspaceVerifier/UnicoreCRM.LeadHandover.WorkspaceVerifier.csproj
```

Requires SQL Server LocalDB by default. Optionally pass a SQL Server connection string after `--`; the login needs database creation permission. The verifier always creates a uniquely named database and drops only that database in cleanup. It never migrates the database named in the supplied connection string.

The original 13 focused checks cover constructor and legacy JSON defaults, canonical serialization, validation at -1/0/1/24/72/168/169 hours, backfill SQL shape, rollback preservation, and the narrow public reader contract. Eight additional checks run real Workspace migrations and the production DI reader against SQL Server, covering persisted legacy JSON backfill, custom policy preservation, workspace isolation, missing workspace, repeated backfill, current policy changes, invalid stored policy rejection, and no entity tracking.

Failures exit nonzero. No global verifier or production registration changes are required.

Six additional reflection checks exercise the production initial-owner upgrade policy. `PreHandoverOwnerCapabilities.txt` freezes the exact owner capability set from backend baseline `1106272021ea0f733c1f610c177e85e907142cac`. Checks cover sorted old-owner admission, an upgrade adding only `leads.handover`, rejection of arbitrary custom subsets and unexpected additions, rejection of `leads.assign` alone, and exclusion of handover from historical predecessor projections. These test the policy admission predicate, not the full AccessControl provisioning transaction.

Four further checks derive the exact pre-Claim and pre-Queue historical sets from that frozen fixture by removing `leads.claim`, then `leads.queue.read`. Both sorted sets must be admitted, while customized subsets missing `tasks.create` must remain rejected. The verifier now contains 31 checks.
