# O4 real-host verification

After the runtime owner releases the shared build/migration lane:

```powershell
./backend/scripts/verify-lead-handover.ps1 -DatabaseName UnicoreCRM_O2Claim_O4_unique_run -RuntimeReady
```

The script requires a fresh LocalDB database and refuses an existing name. It builds the full solution and this verifier, then runs the unchanged O1/O2 SQL/HTTP fixture. The final JSON separates O4 assertions, cumulative HTTP assertions and nine real concurrent HTTP pairings. SQL fixture changes stay in the named isolated database; the database and logs are retained.

The test-only executable registers the same production modules, authentication, workspace middleware, canonical endpoint and SQL participants. It replaces only the internal fault injector and removes background workers so the irreversible boundary remains inspectable. Its loopback recovery control route requires a random process-scoped secret and calls the production recovery runner, including service authorization.

The reservation probe additionally requires the real JWT and trusted workspace middleware. It exercises the production Lead participant: service cancellation fences prevent a late human Reserve without changing the Lead version, while resolving a committed reservation returns its original command/version/audit proof. These are deterministic real SQL participant orderings, separate from the nine concurrent HTTP races.

Recovery injects an exception immediately after the real Tasks transaction commits. The HTTP request must fail, while the Lead still has its previous owner and exact reservation. The script stops and restarts the process, expires the execution lease in SQL, changes Studio SLA, and removes the initiating role's Task capabilities while retaining Lead handover permission. A human retry without the service grant must preserve the reservation and committed Tasks. The script then removes human Lead permission, denies service recovery without its grant, restores that grant and completes recovery without human authority. Task hashes, takeover count, frozen due time, completion audit/event count and final idempotent replay prove persisted recovery without compensation or duplicates.

Limits: lease/retry expiry is accelerated through SQL; this is a process restart after an injected boundary failure, not a power-loss test. Race coverage uses three bounded live HTTP requests per pairing and cannot prove every scheduler ordering. The custom SLA fixture is changed in Studio JSON; range rejection uses the real Studio HTTP validation path. This script verifies the backend O4 matrix and does not certify frontend or the overall O4 phase gate.
