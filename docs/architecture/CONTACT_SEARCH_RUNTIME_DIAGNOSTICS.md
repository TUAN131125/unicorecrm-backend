# Contact search and runtime diagnostics

Investigation on 2026-10-09 retained production search, exact count, Tasks authority, FLS, cursor contract and the two covering indexes. Only disposable benchmark tooling changed. Overall outcome: **PARTIAL_BLOCKED**. SQL search cost is established; concurrent API saturation attribution is **NOT_ESTABLISHED**.

## Search evidence

The contract is arbitrary substring matching after handler trimming and invariant uppercase normalization of the needle. SQL UPPER/CHARINDEX use the actual column collation. FullName is always searched; six optional profile fields are included only when readable. Missing/null/non-scalar JSON values and lax scalar values over 4,000 UTF-16 units produce NULL. Unicode/case/accent parity must use the actual SQL collation, not assumed CLR equivalence. The fixture used SQL_Latin1_General_CP1_CI_AS.

Fresh 10K measurements used one warmup and 20 repetitions per case, canonical EF persistence, captured parameters, IO/TIME and actual plans. Page timing includes authoritative count followed by retrieval. Common/rare/no-match p95 were 18.68/676.05/677.05 ms; corresponding search summaries were 23.13/318.37/334.14 ms. Readable subsets, name, email, phone, short, long and multiple-field cases were also measured.

Previous raw 100K plans explain the skew: no-match count scanned 100K clustered rows (10,543 reads; CPU 3,515 ms versus 493 ms elapsed, parallel). The ordered page scanned about 50K workspace entries and performed 48,277 clustered lookups (149,174 reads; CPU 3,031 ms versus 3,101 ms elapsed). Rare search needed 20,252 lookups to return 26 rows; common stopped after 26. A row-goal estimate of roughly 37 rows favors ordered lookups even when matches are scarce. JSON extraction, uppercase and substring predicates are residual computation; timestamp B-tree ordering cannot seek arbitrary substrings. Workspace filtering limits eligible entries but does not guarantee a narrow clustered count scan. These are previous-run plans, not fresh 100K latency measurements.

Serial authenticated HTTP diagnostics on a fresh 10K fixture confirmed both expensive commands. Across 20 no-match requests, host p50 was 718.01 ms, count p50 337.38 ms and page p50 360.01 ms. Total connection acquisition p95 was 0.85 ms/request and all reader-consumption spans p95 1.55 ms/request. Reader consumption includes materialization and scheduling; mapping/serialization CPU are not separately isolated. Percentiles are not additive. Serial observations do not establish pool, GC, thread-pool or SQL behavior under concurrent load. Count is not the sole dominant bottleneck.

## Rejected experiments

| Candidate | Fresh 10K result | Decision |
| --- | --- | --- |
| Parse readable JSON once with OPENJSON | Common p95 98.99 ms; no-match 494.88 ms; rare 1,066.72 ms | Reject: common regression and root-array semantic mismatch. |
| Disable page row goal globally | Common p95 96.96 ms; rare 656.20 ms; no-match 683.40 ms | Reject: page reads fell from about 14.9K to 1.45K but latency benefit was not established and common regressed. |
| Skip page after zero count | Unmeasured | Reject without contract change: writes may occur between the two live commands. |

Initial object-profile differential testing passed 72 cases at both 10K and 100K. Root-array probes exposed two failures in 72 cases: OPENJSON expands arrays whereas JSON_VALUE at an object property does not. JSON validity permits arrays. The harness records failures and skips timing/adoption of that candidate. **SEARCH_SEMANTIC_PARITY: FAIL** applies to the prototype; production search is unchanged. Fixture FLS subsets do not prove every principal/filter/sort/cursor combination for a new design.

Adaptive hints remain a proposal requiring controlled 100K timing and full parity coverage. Structured projections/substring read models require Contact writer ownership, consistent updates/backfill, workspace/FLS filtering, storage/write measurements and migrations. Computed B-tree values alone do not make arbitrary substring seekable. Full-Text token matching is not a proven substitute. Count removal, on-demand lifecycle and search-semantic changes require API/product decisions; none were adopted.

## Harness and diagnostics

Clients now share a start barrier. Requests start in `[5,35)` for the measured cohort; RPS counts cohort completions by 35 seconds divided by exactly 30 seconds. Warmup carryover is excluded and late completions are reported separately. HTTP timing ends after body receipt before cursor parsing. Endpoint labels follow actual exhausted-cursor resets; each client owns its cursor. CPU/allocation scope includes warmup/drain and is labeled. Six synthetic checks verify accounting/barrier behavior, not network capacity.

Mixed/light/search/summary/Kanban workloads use distinct pass identifiers and fresh output. SQL diagnostics use a separate pool and sample database requests, sessions, grants and cumulative session waits without instance resets. Interpret waits as per-session deltas, not sums of cumulative snapshots. Missing memory monitoring fails closed; seed guards run between batches and before measurement. No pool, worker, SQL memory or production configuration changed.

The optional standalone startup hook observes only an opted-in fixture child. Bounded rotating snapshots store synthetic IDs, command categories, acquisition/reader spans, counters and coverage. No SQL, values, tokens or bodies are stored. Hosting duration excludes pre-pipeline queueing; acquisition includes more than pool wait; counters require provider units. Its README documents these limits.

Observed/unobserved serial p95 differences ranged from -5.37% to +4.76%. Sequential shared-host runs do not isolate observer overhead. Coverage: 105 HTTP records, 1,449 each connection/command/reader records, zero dropped records and no pending operations/readers/requests at final flush.

## Resource limits and validation

100K seeding and initial differential testing completed, but fresh timing stopped at 1,725,692 KiB free RAM. All six 25-client workloads lacked the 2 GiB reserve (2,007–2,016 MiB available); no load stage ran and higher stages were not attempted. The 1,536 MiB guard for bounded serial diagnostics did not lower concurrent guards. 500K and 10K concurrency remain unmeasured. These limits explain skipped experiments, not the previous saturation's cause.

Backend, harness and hook builds passed without warnings. Harness checks, canonical Contact HTTP (551 per fixture), access (569), cursor (17), Tasks follow-up and Lead Kanban verification passed. Nine GUID-owned fixture databases were cleaned after marker/server/managed-file/manifest/session checks; final metadata showed none remaining. No business database, Development configuration, migration, frontend, DataProtection or deployment infrastructure changed.

Raw plans, requests, manifests, resource stops, parity failures and telemetry remain external evidence. Reproduce using the benchmark README. A host with sufficient guarded headroom is required for fresh 100K timing and controlled concurrent attribution. No production performance improvement or sustained capacity is claimed.
