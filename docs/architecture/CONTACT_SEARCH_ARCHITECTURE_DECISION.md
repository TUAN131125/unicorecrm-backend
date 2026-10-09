# Contact Search Architecture Decision

Status: accepted decision to retain the current production baseline. Date: 2026-10-09.

This closes the current search investigation and returns development to admitted CRM workflows. It does not certify search latency at scale or production capacity. The backend has no numbered ADR directory; this record follows its existing `docs/architecture` topic convention.

```text
CONTACT_SEARCH_PRODUCTION_ARCHITECTURE: CURRENT_BASELINE
NEW_PROJECTION: NOT_ADMITTED
QUERY_ONLY_REWRITE: NOT_ADMITTED
SEARCH_SEMANTICS: PRESERVED
SEARCH_PERFORMANCE_RISK: OPEN
FUNCTIONAL_SEARCH: IMPLEMENTED
SEARCH_PERFORMANCE_AT_SCALE: UNRESOLVED
```

## Context and evidence

Source inspection confirms `ContactListSql.Filter` still uses parameterized workspace/owner predicates and exact `CHARINDEX(needle, UPPER(value))` matching. `ListContacts.Handler` determines readable fields and trusted record scope, protects cursors and resolves Tasks authority. `EfContactsPersistence` performs an exact count followed by a separately ordered bounded page; there is no snapshot promise. Canonical `ContactsDbContext` retains the two lifecycle-covering indexes and has no research trigger or OUTPUT setting. Experimental SQL rewriting and `UseSqlOutputClause(false)` exist only in the benchmark.

At 100K Contacts, paired combined persistence-call p95 was:

| Query | Baseline ms | Clustered candidate ms |
| --- | ---: | ---: |
| Common | 37.23 | 114.23 |
| Rare | 1,979.39 | 1,041.46 |
| No-match | 4,046.05 | 949.84 |

These shared-host measurements exclude HTTP authorization/serialization and are not endpoint SLOs. Reverse-order common repeated 33.48 versus 122.47 ms. The unhinted rare plan retains 20,252 qualifying Contact lookups and adds 26 full-row seeks; the clustered variant changes qualification to a scan. Rare page logical reads fall from 62,785 to 11,005, while count remains 10,549. Sparse-search benefits do not authorize the common-search regression. Locking READ COMMITTED qualification/materialization concurrency parity remains NOT_ESTABLISHED.

The final investigation measured 63 cases at each of 10K/100K. Both candidates passed 98 controlled differential cases per tier and three sequential live-page scenarios. Those checks do not establish every public authorization path or intra-command concurrent interleaving.

## Alternatives and disposition

| Alternative | Evidence and reason | Disposition |
| --- | --- | --- |
| Existing Contact index coverage | Lifecycle count/owner predicates gain covering reads; clean install/upgrade/down/reapply fingerprints were verified. Arbitrary JSON substring remains expensive. | Retain admitted existing indexes; no new index in this closure. |
| SQL row-goal hints | Global page row-goal disabling regressed common 10K p95 to 96.96 ms without an established overall benefit. | Rejected; no runtime hint heuristic. |
| OPENJSON extraction | Root arrays expand unlike named-root JSON_VALUE; differential failures and common regression. | Rejected semantic substitution. |
| Scalar Search Projection | 10K rare/no-match improve to approximately 191/194 ms, common increases 19.23 to 25.60 ms; used storage adds 2,392 KiB (+14.32%). Synchronous writers/backfill/migration admission incomplete. | NOT_ADMITTED. |
| Unhinted query-only late materialization | Optimizer retains qualification lookups; no stable general benefit, with a second Contact read. | NOT_ADMITTED. |
| Clustered candidate selection | Sparse-search gain with repeated common regression; concurrent reread parity unproven. | NOT_ADMITTED. |
| EF-compatible SQL trigger | Canonical UPDATE/ARCHIVE OUTPUT reproduces error334; isolated model with UseSqlOutputClause(false) passes EF lifecycle/concurrency/rollback. Public writer, audit/outbox/idempotency and deployment proof remain partial. | Isolated feasibility only; no production model/trigger change. |

Full-Text tokens, prefix matching, n-grams and zero-count page skipping are not admitted replacements. This decision creates no new projection, hint, trigger, migration or provider configuration.

## Required semantics and security

Preserve handler trimming, blank-as-absent, maximum 200 UTF-16 units, invariant-uppercase needle and actual SQL collation behavior. FullName is searched; the six optional profile fields participate only when independently readable. Preserve lax JSON_VALUE behavior for missing/null/non-scalar/over-4,000-unit values, duplicate keys and property case. Do not union hidden-field candidates or search client-side across unauthorized data.

Workspace is trusted server authority. Apply record OWN scope, archive/status/profile/relationship filters before items/count/summary; never cache aggregates across principals. Keep exact authorized counts and status summaries, three keyset sorts with ID tie-breakers and live-page behavior. Cursors remain protected and bound to actor/workspace/filters/readable fields/Tasks scope/timezone/expiry. Follow-up is Tasks-derived under independent Tasks authority; unavailable authority fails closed for dependent filtering/sorting. No Activity- or Deal-derived projection is enabled.

## Consequences and open risks

Rare/no-match substring search remains slow at 100K. B-tree ordering cannot seek arbitrary substrings; SQL JSON/scalar residual CPU, sparse qualifying lookups and Tasks aggregation remain costs. No workload weights, accepted common-search regression, write/storage budget or production SLO has been supplied. Projection write cost, online backfill, rollback and complete public writer consistency are unresolved. API saturation cause and 10K concurrent users remain NOT_ESTABLISHED.

## Reopening and production admission

Reopen only with an approved product/architecture question and a specific testable hypothesis, or a confirmed current correctness/security defect. A defect is a separately scoped repair, not permission for speculative search redesign. Performance evidence must come from an isolated environment with adequate guarded headroom and representative workload weights/SLOs.

A new design must demonstrate exact SQL/collation/JSON/FLS parity, authorized count/summary and all cursor/sort/Tasks cases, coordinated live-write qualification/materialization behavior, controlled 100K+ read distributions/actual plans/repeats, and acceptable common/rare/no-match tradeoffs. A persistent projection additionally requires every canonical/public writer, audit/outbox/idempotency/concurrency/rollback, exact version/value maintenance, storage/log/write cost, safe install/upgrade/online backfill/switch/rollback and independently reviewed release evidence. Isolated EF compatibility or a benchmark PASS alone is insufficient.

## Evidence and reproducibility

- [Index coverage evidence](CONTACT_SQL_PERFORMANCE.md).
- [Search/runtime diagnostics and rejected semantic/hint experiments](CONTACT_SEARCH_RUNTIME_DIAGNOSTICS.md).
- [Scalar projection read/write/storage proof](CONTACT_SEARCH_ARCHITECTURE_PROOF.md).
- [Final query-only and isolated EF feasibility](CONTACT_SEARCH_FEASIBILITY.md).
- [Benchmark commands and safety rules](../../scripts/SqlPerformanceBench/README.md).
- External retained bundle: `review-artifacts/search-feasibility-20261009/CONTACT_SEARCH_FEASIBILITY_REPORT.json`, `query-analysis.json`, `proof/` and `ef-final/` under the enclosing workspace. Raw plans, SQL, samples and cleanup manifests are intentionally outside Git.

For an explicitly approved future investigation, the existing commands are `dotnet run --project scripts/SqlPerformanceBench -- <test-server> <fresh-absolute-output> 100000 20 --search-feasibility` and the separate 10K `--ef-trigger-feasibility` mode. They are references, not instructions to run in this product-readiness task. This closure creates no database and runs no performance experiment.
