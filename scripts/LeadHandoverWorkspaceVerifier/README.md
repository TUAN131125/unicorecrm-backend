# Lead Handover Workspace verifier

Run from the backend directory:

```powershell
dotnet run --project scripts/LeadHandoverWorkspaceVerifier/UnicoreCRM.LeadHandover.WorkspaceVerifier.csproj
```

Requires SQL Server LocalDB by default. Optionally pass a SQL Server connection string after `--`; the login needs database creation permission. The verifier always creates a uniquely named database and drops only that database in cleanup. It never migrates the database named in the supplied connection string.

The original 13 focused checks cover constructor and legacy JSON defaults, canonical serialization, validation at -1/0/1/24/72/168/169 hours, backfill SQL shape, rollback preservation, and the narrow public reader contract. Eight additional checks run real Workspace migrations and the production DI reader against SQL Server, covering persisted legacy JSON backfill, custom policy preservation, workspace isolation, missing workspace, repeated backfill, current policy changes, invalid stored policy rejection, and no entity tracking.

Failures exit nonzero. No global verifier or production registration changes are required.

Reflection checks exercise the production initial-owner upgrade policy and custom-role catalog. `PreHandoverOwnerCapabilities.txt` freezes the exact owner capability set from backend baseline `1106272021ea0f733c1f610c177e85e907142cac`. The current owner set must equal that fixture, human `leads.handover` must be absent and unassignable, and `leads.assign`/`tasks.assign`/`tasks.create` remain assignable. Arbitrary custom subsets and unexpected additions cannot receive owner upgrades. These test the policy admission predicate, not the full AccessControl provisioning transaction.

Historical pre-Claim and pre-Queue sets derive from that frozen fixture by removing `leads.claim`, then `leads.queue.read`. Both sorted sets must be admitted, while customized subsets missing `tasks.create` remain rejected.

Real AccessControl migration checks seed existing owner, custom, inactive and unaffected roles before applying `20261001100000_RemoveHumanLeadHandoverCapability`. They prove exact human-grant removal, preservation of unrelated permissions and recovery service authority, owner convergence, one directory revision advance per affected workspace, preservation of untouched seed identity, and safe repeated cleanup. The data-only migration leaves the EF model unchanged; its rollback deliberately does not recreate obsolete authority. Old migration history remains intact.
