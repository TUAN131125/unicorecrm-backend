# O4 real-host verification

After the runtime owner releases the shared build/migration lane:

```powershell
./backend/scripts/verify-lead-handover.ps1 -DatabaseName UnicoreCRM_O2Claim_O4_unique_run -RuntimeReady
```

The script requires a fresh LocalDB database and refuses an existing name. It builds the full solution and this verifier, then runs the unchanged O1/O2 SQL/HTTP fixture. The final JSON separates O4 assertions, cumulative HTTP assertions and nine real concurrent HTTP pairings. SQL fixture changes stay in the named isolated database; the database and logs are retained.

The test-only executable registers the same production modules, authentication, workspace middleware, canonical endpoint and SQL participants. It replaces only the internal fault injector and removes background workers so the irreversible boundary remains inspectable. Its loopback recovery control route requires a random process-scoped secret and calls the production recovery runner, including service authorization.

The reservation probe additionally requires the real JWT and trusted workspace middleware. It exercises the production Lead participant: service cancellation fences prevent a late human Reserve without changing the Lead version, while resolving a committed reservation returns its original command/version/audit proof. These are deterministic real SQL participant orderings, separate from the nine concurrent HTTP races.

Recovery injects an exception immediately after the real Tasks transaction commits. The HTTP request must fail, while the Lead still has its previous owner and exact reservation. The script stops and restarts the process, expires the execution lease in SQL, changes Studio SLA, and removes the initiating role's Task capabilities while retaining Lead assign permission. A human retry without the service grant must preserve the reservation and committed Tasks. The script then removes human Lead assign permission, denies service recovery without its grant, restores that grant and completes recovery without human authority. Task hashes, takeover count, frozen due time, completion audit/event count and final idempotent replay prove persisted recovery without compensation or duplicates.

Limits: lease/retry expiry is accelerated through SQL; this is a process restart after an injected boundary failure, not a power-loss test. Race coverage uses three bounded live HTTP requests per pairing and cannot prove every scheduler ordering. The custom SLA fixture is changed in Studio JSON; range rejection uses the real Studio HTTP validation path. This script verifies the backend O4 matrix and does not certify frontend or the overall O4 phase gate.

Canonical ingress is `POST /workflows/lead-handover/{leadId}` with only `nextOwnerId` and `reason`. Obsolete route and unmapped policy/task-target/old-owner fields are rejection fixtures only. Human admission requires `leads.assign`, `tasks.assign`, and `tasks.create`; every eligible Task is transferred automatically.

The OWN regression gives actor A OWN scopes for both Leads and Tasks and makes A own every eligible Task. Initial A to B must return HTTP 200 and durable completion. A later replay retains current resource capabilities and OWN scopes, returns the exact stored outcome, and leaves Task SQL unchanged. Recovery replay similarly permits OWN scope after transfer while still denying revoked resource capabilities.

Readiness is source-only until main explicitly releases the lane. The reservation probe matches the production participant contract: `NextOwnerId` and no policy argument. No build or execution is implied by these assertions.

Main's Lead projection uses captured `LeadHandoverPreparation.Authorization` through synchronous `Project`; fresh preparation accepts optional admission. The probe calls only Reserve/Resolve/Release and requires no projection adapter. HTTP OWN coverage exercises the production handler's captured admission, while replay denial checks individually revoke each required resource capability and hide Task proof `dueAt`.
