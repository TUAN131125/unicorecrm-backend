# Tasks Lead Handover verifier

Run from the repository root:

```powershell
dotnet run --project backend/scripts/TasksHandoverVerifier/TasksHandoverVerifier.csproj
dotnet run --project backend/scripts/TasksHandoverVerifier/TasksHandoverVerifier.csproj -- --sql
```

The isolated project links the Tasks production participant/domain/persistence source so no production assembly visibility or global verification scripts need changing. The in-memory checks supply AccessControl decisions at the existing owner boundary; they do not prove the AccessControl grant configuration. The SQL checks use real Tasks migrations and EF persistence against a uniquely named LocalDB database, removed after the run.

Coverage: KEEP/MOVE, complete-set record authorization before mutation, capability denial, frozen takeover semantics, excluded records, idempotency conflict/replay, stable proof IDs, service-authorized recovery replay, denial of service admission without an existing Tasks commit, refreshed state after preflight, concurrent execution, write-failure rollback, and eligible range locking.

Workflow integration must call `ValidateAsync` with current human context before anchor/reservation and `ExecuteAsync` with human context before the first Tasks commit. Service recovery uses `svc_lead_handover_recovery` and `leads.handover.recover`; with no stored Tasks participant record it returns `HANDOVER_TASKS_NOT_COMMITTED` (409). AccessControl service grants and the full workflow crash-after-Tasks/before-Leads scenario belong to the coordinator verification.

Before releasing a reservation, call service-authorized `ResolveOrFenceAsync` with the frozen command. Success returns the exact committed participant result and requires continuing completion. `HANDOVER_TASKS_FENCED` (409) proves a durable cancellation under the same Serializable key-range lock used by execution; only this result permits release. Other errors do not permit release. Repeated fences retain the original audit/outbox proof. A paused human execution observes the stored cancellation before traversing takeover identifiers or mutating Tasks.

Use human-only `AuthorizeReplayAsync` before disclosing completed proof. It enforces current Tasks capabilities and every recorded Task's current record scope, and never creates Tasks when proof is absent.

The SQL verifier includes deterministic pauses before the human's first key-lock read and after its SQL writes but before transaction commit. These prove both fence-first and commit-first reconciliation orderings.
