# Contact SQL performance evidence

The October 2026 synthetic SQL benchmark identified avoidable clustered reads in exact Contact counts and status summaries. The query filters workspace, lifecycle status and archive state before counting. Existing workspace/timestamp and workspace/owner indexes did not cover these predicates.

Two existing indexes now include lifecycle columns, preserving their original key order and index count:

- `(WorkspaceId, UpdatedAt, ContactId)` includes `Status, ArchivedAt, OwnerId`.
- `(WorkspaceId, OwnerId, CreatedAt, ContactId)` includes `Status, ArchivedAt`.

Canonical migrations `20261008142735_ContactListLifecycleIndexCoverage` and `20261008143512_ContactOwnerLifecycleIndexCoverage` drop and recreate only these indexes. Down restores their original definitions. Query code, exact-count lifecycle, substring fields, authority predicates and cursor semantics are unchanged.

The final before/after run used 100,000 Contacts, 100,000 Leads, 145,100 Tasks and 25,000 relationships across 26 workspaces. Each case had one warmup and 20 timed persistence calls; command replays and actual-plan instrumentation were separate. These are shared desktop SQL measurements, excluding HTTP authorization and serialization. With 20 samples p99 is the maximum, not a precise production tail estimate.

| Operation | Baseline p95 ms | Final p95 ms | Count/summary logical reads before → after |
|---|---:|---:|---:|
| First Contact page | 28.02 | 31.33 | 10,546 → 1,318 |
| Status summary | 54.03 | 58.89 | 10,546 → 1,318 |
| Status-filtered page | 63.88 | 15.40 | 10,546 → 1,318 |
| Owner-filtered page | 82.83 | 6.10 | 10,546 → 70 |
| Owner summary | 15.61 | 3.92 | 10,546 → 70 |
| Rare substring | 2,035.59 | 1,960.74 | 10,546 → 10,543 |
| No-match substring | 4,208.62 | 4,049.01 | 10,546 → 10,543 |
| Next-follow-up sort | 257.66 | 250.97 | 10,546 → 1,318 |

Lower I/O does not guarantee lower latency in every case. First-page and unfiltered summary p95 increased; unchanged Lead queries also varied significantly between runs. The combined JSON/follow-up case increased total logical reads from 16,065 to 24,091 despite lower p95. Review the complete comparison, not selected wins. Actual plans show covered workspace/owner seeks for applicable counts; page enrichment still performs bounded lookups and Tasks aggregation. The stable evidence is reduced lifecycle-count I/O and a substantial owner-path latency improvement; no formal product SLO was supplied.

Persisting six normalized JSON search expressions was rejected: the 10K rare-search latency improved but widened clustered rows roughly doubled scan reads. Including computed search values in a wide timestamp index was also rejected: at 100K no-match improved from 4,209 to 1,212ms, but common-search p95 increased from 33 to 117ms and common-search summary from 40 to 167ms. No computed columns, Full-Text substitution or prefix semantics were introduced.

Arbitrary substring/JSON extraction and Tasks-derived aggregation/sorting remain expensive. Lead column counts still scan substantial data. No Tasks or Lead indexes were added without isolated evidence. Exact counts still execute before continuation; there is no cross-principal aggregate cache or snapshot promise.

Includes enlarge index leaf storage and add maintenance when status, archive state or owner changes. A narrower timestamp-only prototype grew from 984 to 1,360 KiB at 10K; that is not the final two-index storage cost. Rebuild fragmentation can obscure comparisons. Clean install and 10K/100K upgrade/down/reapply passed with identical all-column Contact fingerprints; the 100K combined index upgrade took about 977ms initially and 1,763ms on final verification. Migration DDL can block writers and consume transaction log space; production maintenance-window impact and 500K rebuild cost remain unmeasured. No production/development business database was migrated.

Full 10K and 100K suites completed. A 500K baseline was seeded and partially measured, then stopped when available physical memory approached 1 GiB; its remaining cases and optimized counterpart were not measured. All task-owned SQL resources were tracked by manifests and cleaned after identity/file/session checks. Production DataProtection topology is outside this work's scope.

Reproduce with `scripts/SqlPerformanceBench` using a fresh absolute evidence directory outside Git and a verified development/test SQL instance. Raw SQL, synthetic parameters, actual `.sqlplan`, IO/TIME messages, timing samples and index/statistics inventories belong in external review artifacts. The benchmark README documents safety checks and verification modes. Public Contacts/access-control, Tasks/follow-up and Lead Kanban verifiers supplement the fixture-level cursor tests; author review is not an independent release attestation.


A separate disposable 10K HTTP fixture also ran 25/100/250/500-client stages without HTTP errors or timeouts. Aggregate p95 was 171/775/2,578/5,268ms; RPS was 41/113/108/91. These 30-second sampling targets used one API replica and 500ms think time. Throughput saturation and increasing latency prohibit a production-capacity claim. The 1000-client stage lacked its 8GiB free-memory reserve and was skipped. SQL CPU permissions, wait attribution and thread-pool starvation were not established. This is not a 10K-concurrency proof or soak test.
