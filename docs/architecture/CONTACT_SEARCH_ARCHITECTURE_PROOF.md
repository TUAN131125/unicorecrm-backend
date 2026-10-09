# Contact search architecture proof

This investigation preserves arbitrary substring semantics, exact authorized counts, summaries, server keyset pages, Tasks authority and the current API. The isolated prototype is not registered in production DI and creates no canonical migration. Production promotion is **NOT_ADMITTED**. A 10K result does not prove 100K scale.

## Source contract and ownership

`ListContacts/Handler.cs` trims whitespace, treats blank search as absent and rejects search longer than 200 UTF-16 units. `ContactListSql.cs` normalizes the needle with CLR `ToUpperInvariant`; SQL uses `CHARINDEX(needle,UPPER(value))>0` with actual column collation. FullName is always searched; displayName/workEmail/personalEmail/mobilePhone/workPhone/otherPhone are admitted independently by `CanRead`. `JSON_VALUE` lax scalar semantics apply, including NULL for missing, null, object, array and over-4,000-unit values. Duplicate keys and property casing remain SQL behavior. No CLR parser or OPENJSON substitutes for these facts.

Trusted Workspace and record-owner scope are applied before items/count/summary. Canonical status/archive, owner, profile, customer relationship and Tasks filters stay in the same query. Exact count and page remain separate live commands, with no snapshot promise. Timestamp/name/follow-up ordering and ID tie breakers are unchanged. Protected Handler cursors remain bound to principal, workspace, filters, readable fields, Tasks scope, timezone bounds and expiry.

The Contact aggregate owns name/profile/email identity changes. Public commands and Lead qualification share Contacts persistence and SERIALIZABLE transactions. Any future projection writer must cover all admitted writers, optimistic concurrency, audit/outbox/idempotency rollback and relationships affecting version. A benchmark SQL trigger is not proof of every public command.

## Options

| Strategy | Benefit | Costs and risks | Decision |
| --- | --- | --- | --- |
| A: exact candidate IDs / late materialization | Could delay full Contact lookups until qualifying IDs are known | Count still evaluates JSON; SQL optimizer can flatten composition; forcing broad page plans already regressed common search | Proposal; requires actual alternative plans and stable selection evidence |
| B: separate SQL scalar projection | Removes repeated JSON extraction without widening authoritative Contact clustered rows | Additional scalar storage, synchronous writes and joins; complete consistency/backfill required | Selected benchmark prototype, not promoted |
| C: substring candidate index plus canonical residual | Can reduce rare/no-match candidates | Must prove no false negatives under actual SQL collation; short needles require exact fallback; token entries/write cost can exceed scalar projection | Proposal; collation-safe superset unproven, no production index |

Full-Text token substitution, zero-count page skipping, OPENJSON array expansion and global row-goal hints remain rejected from previous evidence. An n-gram design needs field/Workspace-aware retrieval and an exact residual; a hidden-field global candidate union is forbidden. Storage bounds must count tokens, keys and indexes rather than assume free duplication. No workload weights or write/storage acceptance envelope were supplied.

## Selected prototype

`SearchArchitecture.cs` creates a benchmark-owned table with one composite `(WorkspaceId,ContactId)` primary key, a matching existing Contact alternate-key FK, SourceVersion and seven separate scalar columns. Every value is computed by the original SQL UPPER/JSON_VALUE expressions with explicitly copied source collation. Query rewriting changes only searchable expressions and adds the unique Workspace/Contact join; authorization/filter predicates and the original ordering/summary/count composition remain canonical. There is no token normalization algorithm and no n-gram false-negative problem in this prototype. Missing or stale projection rows would still cause false negatives, so synchronization is an admission requirement.

A set-based fixture trigger synchronizes insert/update/delete in the authoritative transaction. Archived projections remain available for explicit archived-status queries. FK cascade prevents orphans. Initial table/trigger/backfill installation holds a Contact table lock in one SERIALIZABLE transaction, so partial projection contents are not exposed. This deliberately blocking approach is not an online migration claim. A row-locked backfill/live-write test checks final exact values/version; competing-command incompletion at 100 ms alone does not establish DMV-attributed lock waits.

The fixture tests create, rename, email/phone clear/profile replacement, owner/archive changes, rollback, stale-version competing updates, multirow maintenance and failed constraints. EF create/update/archive compatibility is tested separately; SQL trigger behavior alone cannot certify the current EF OUTPUT path. No eventual consistency is admitted.

## Measured evidence and decision

On 10K, the initial paired persistence p95 comparison was common 19.23 to 25.60 ms, rare 688.81 to 191.20 ms, and no-match 686.91 to 194.36 ms. Reverse-order common repeated 20.42 to 26.90 ms. Name-only increased from 84.14 to 114.40 ms. Rare/no-match page logical reads rose from about 14.9K to 29.7K while count reads fell from 847 to 239. Lower extraction CPU explains the latency benefit; this is not elimination of sparse-page lookup cost. Read plans include IO/TIME, rows, grants and spill evidence. Main Unicode performance case is an ASCII-fixture no-match; the separate focused fixture measures an actual Vietnamese match.

Projection used pages were 2,392 KiB / 10,000 rows; Contact plus its indexes used 16,704 KiB before installation, an additional 14.32%. This is measured used-page storage, not allocated database file growth or a 100K extrapolation. One row per Contact does not imply zero write amplification: fixture trigger updates replace that row and maintain its key/FK.

Write samples use one warmup and 20 raw SQL transactions per operation and mode. Transaction duration includes begin/commit and a log-usage diagnostic query; CPU/read/page-write deltas include diagnostic SQL. Log bytes are distinct from session page writes. These results cannot be presented as public command latency. Archive tail variability and common/name-only read regressions require explicit acceptance rather than a fabricated SLO.

Differential testing uses canonical SQL/persistence with controlled readable-field inputs: 76 JSON/Unicode cases and 22 pairwise scenarios, including three sort traversals to page ten. Root-array probes are checked as identities/status without domain-deserializing malformed typed profiles. The final focused run also tests protected Handler cursors with explicit fixture policies; that is not full public HTTP policy admission.

The external report distinguishes completed datasets and resource stops, repeated-run variance, SQL mutation evidence from public-command proof, and all unavailable acceptance gates. Production remains unchanged because improvement in rare/no-match does not authorize the observed common/name-only tradeoff, full command/policy/migration evidence is incomplete, and guarded 100K proof is required. API saturation and concurrent load are outside this round.

## Migration admission still required

A production design would need canonical EF model/migration ownership, clean install and upgrade validation, exact backfill fingerprints, public writer compatibility, recoverable failure/retry and transactional rollback. It must prevent missing rows while an inner-join query becomes active, demonstrate live-write convergence, assess table-lock duration/log growth at realistic size, and provide a safe switch/rollback preserving authoritative Contacts. Dropping the prototype schema preserves the source rows but is not a tested production migration rollback. No business database is migrated by this harness.

Reproduce with `scripts/SqlPerformanceBench/README.md`, fresh external evidence output and verified managed SQL DATA paths. All changes remain benchmark/documentation only; resource guards, ownership manifests and cleanup checks apply to every run.

Final focused evidence found an explicit compatibility blocker: EF create passed, but EF name update failed with SQL error 334 because the current SQL OUTPUT path conflicts with the enabled trigger. Archive through EF was NOT_RUN after that failure. Raw SQL consistency PASS does not override this failure. Production would need a separately reviewed compatible persistence strategy; none was implemented. The 100K tier was NOT_RUN_RESOURCE_LIMIT at 1,486,168 KiB available RAM against its 2 GiB reserve.

Raw profile-write samples also exclude normalized-email identity index maintenance, so they measure projection maintenance rather than complete Contact command write amplification. No acceptable production write-cost claim is made.

The follow-up [contact search feasibility investigation](CONTACT_SEARCH_FEASIBILITY.md) independently evaluates query-only candidate selection and an isolated trigger-compatible EF model. It preserves this round's measurements and limitations; later raw-write fixtures include normalized-email identity maintenance. No production promotion follows from isolated compatibility.
