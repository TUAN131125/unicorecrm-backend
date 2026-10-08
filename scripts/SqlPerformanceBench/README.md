# SQL performance benchmark

Runs the current Contacts and Lead Kanban persistence methods against synthetic data in a newly allocated SQL Server database. It does not read application configuration, provision SQL logins, change server settings, or mutate an existing database.

```powershell
dotnet run --project scripts/SqlPerformanceBench -- <server> <absolute-output-directory> 500000 20
```

The integrated Windows identity needs database creation, migration, execution-plan and resource-DMV permissions. Prefer a dedicated test instance/identity. Evidence must be outside the repository. The server must report a managed Microsoft SQL Server DATA directory for both default paths; user/root/repository defaults are refused. No LocalDB default is assumed safe.

The run allocates `UniCoreCRM_PerfBench_<GUID>`, checks absence, records its manifest and actual file paths, and writes an ownership marker before migrations. Cleanup validates server identity, marker, file manifest and lack of other sessions. It does not force-close sessions. Failed revalidation retains the database and records the reason. An interrupted/killed process may also leave a manifest-owned resource for separately verified cleanup.

Each tier checks available memory, volume space and other active user requests. Reserves are 1 GiB free memory for 10K, 2 GiB for larger tiers, and 8 GiB free disk plus a conservative 12 KiB per Contact/Lead pair. Up to eight other active user requests are allowed; measurements on such a host are shared-host evidence. No instance-wide cache flushing or configuration changes occur.

Seed batches contain at most 5,000 Contact/Lead pairs. Deterministic integer arithmetic is the seed; no random generator or personal data is used. The distribution is 50% in one large workspace, 30% across five medium workspaces, and 20% across twenty small workspaces. There are 23 owners, varied status/profile fields, about 25% linked Contacts and a mixture of zero/two/five tasks. Completed, cancelled, archived and foreign-module tasks exercise projection eligibility. Duplicate names, timestamps and due dates are deliberate. Data grows incrementally through 10K, 100K and optionally 500K rows per entity; the generator is not a business write API.

An interceptor captures actual EF commands and synthetic parameters from the production persistence methods. The harness records combined persistence-call latency, then replays each command separately for latency, actual XML execution plans and STATISTICS IO/TIME. Replay uses the original parameter types/sizes. Measurements exclude HTTP authentication/audit/serialization and must not be presented as endpoint or production-capacity measurements.

There is one explicit warm-up and at least twenty measured iterations per operation/command. Percentiles use nearest rank; p99 with twenty samples is the maximum and has limited tail precision. The first observation is recorded separately, but is **not a guaranteed cold-cache run**: shared SQL caches are never cleared. Plan instrumentation samples are separate from timed repetitions. QueryTimeStats CPU/elapsed and memory/spill evidence are one actual-plan sample, not CPU percentiles. Physical/read-ahead reads are available in the raw IO files.

`actualRowsReadAcrossOperators` and `actualRowsAcrossOperators` sum operator counters and may count a row at several operators; they are not distinct source rows. Inspect the saved XML for operator-level counters, lookup execution counts, estimates, grants and parallelism. Logical reads sum the IO messages, including worktables. Summaries preserve their authorized scope. The fixture's Tasks authority is constructed for controlled persistence measurements; public authorization is covered separately by the repository's HTTP/follow-up verifiers.

Deep traversal follows actual keyset positions for pages 2, 10 and 100. It checks duplicate IDs among traversed Contact windows. This does not promise frozen snapshots or prove all cursor-security cases. Output includes actual plans, SQL, synthetic parameters, raw IO, distributions, index/statistics inventory, schema migrations, latency samples and ownership/cleanup state. Keep generated outputs and database files out of Git.

No application-name, key-ring, AI credential or webhook secret configuration is changed. Production DataProtection topology remains outside this benchmark's scope.

Verification and experiment modes accept a fifth argument: `--migration-verification`, `--cursor-verification`, `--count-index-experiment`, `--owner-index-experiment`, `--search-projection-experiment`, or `--indexed-search-experiment`. Experiments alter only the newly created disposable database and retain their before/after plans. The two search experiments are research fixtures, not proposed production schema. Migration verification seeds the baseline schema, upgrades, rolls back and reapplies; streaming fingerprints compare all Contact columns. Cursor verification exercises the actual handler with explicit fixture authority and ephemeral keys.

Resource checks also run before each measured case. A resource stop preserves completed samples and does not mark unmeasured cases successful. The default maximum is 100K; 500K is opt-in. Developer/Express SQL editions are required. No tier represents production capacity.

The optional `--api-load <absolute-output-directory>` entry point requires an already running disposable loopback API fixture. Its private process environment must provide `PERFBENCH_API_URL`, `PERFBENCH_RUN_ID`, `PERFBENCH_WORKSPACE`, `PERFBENCH_API_PID`, `PERFBENCH_TOKEN`, and `ConnectionStrings__UnicoreCRM`. It checks integrated authentication, the GUID database name and ownership marker. Keep the token out of command arguments/logs. The owner of the fixture remains responsible for verified cleanup after its API process exits. This helper never creates or drops databases.

HTTP stages attempt 25, 100, 250, 500 and 1000 simulated clients with 500ms think time, 5s warmup and a 30s sampling target. Stage memory reserves increase from 2 to 8 GiB; a failed stage stops escalation. Each client maintains separate Contact and Lead cursors. The fixed mix is 30% first page, 20% Contact continuation, 10% search, 10% owner, 10% summary, 10% overdue, 5% Lead first and 5% Lead continuation. Results include RPS, full response latency, errors and process/resource observations. SQL waits and thread-pool starvation are not established by this helper. This short run is not a soak or a 10K-concurrency proof.

See `docs/architecture/CONTACT_SQL_PERFORMANCE.md` for the measured index decision and remaining bottlenecks.

To compare the same current query code with the pre-coverage index schema, add `--baseline` as the fifth argument. The harness downgrades only its own fresh database to `20261006144337_ContactListReadProjectionIndexes` before seeding. Run again without that flag into a different evidence directory for final indexes. Experiment modes also start from this baseline index schema; no user checkout reset is necessary.
