# Authoritative Deal pipelines

## Authority and design

The explicit dynamic pipeline task admits replacing the formerly BLOCKED pipeline configuration operations and moves ownership from the placeholder Workspace configuration contract to Deals. Studio is a consumer. This is a HIGH risk schema, lifecycle and public contract change. Existing checkout changes are preserved in the task-runtime baseline.

Deals owns pipelines, stages, their configuration transaction and stage eligibility. Workspace supplies trusted tenant/member context; AccessControl supplies `studio.read`/`studio.configure` (configuration) or `deals.read` (CRM consumption). No configuration write is authorized by a CRM read permission.

Pipeline IDs and stage IDs are stable opaque identities. Existing codes/categories are immutable. Every pipeline retains exactly one active system WON and LOST outcome and at least one active OPEN stage. Existing open stages may be retired only when no Deal (including archived/historical records) references them. Stages can be removed only when unreferenced and nonterminal. A default pipeline cannot be inactive. Setting a new default is atomic, increments both affected pipeline versions, and never moves Deals.

One owner-local SQL transaction and workspace registry lock serialize configuration mutation with Deal creation/stage selection. Idempotency replay follows current authorization but precedes mutable business validation. Mutations retain audit, outbox and replay evidence in the existing Deals stores. Reads use one bounded registry query per workspace, not per card. No browser fallback in Connected mode.

The registry persists compatibility policies: existing SOLUTION/PROPOSAL have BEST_CASE forecast floors, NEGOTIATION has COMMIT, and PROPOSAL/NEGOTIATION require positive amounts. Custom stages use score-based forecast thresholds (50/80) with PIPELINE floor and ordinary required profile fields; display order and default probability do not change Opportunity Score. These compatibility policies are server-owned and cannot be changed through display configuration. Default probability remains metadata. No automatic forecast/history rewrite occurs on configuration changes.

## Migration and provisioning

An additive Deals migration creates owner tables, seeds one deterministic default per workspace represented in existing Deals, and backfills only PipelineId. It checks known legacy stages before writing; unknown stage data fails closed. Existing stage, category, version, profile and history values are untouched. Composite foreign keys enforce workspace/pipeline ownership. Workspaces with no Deals receive the same default through an idempotent owner participant during provisioning, or authenticated first registry access for older workspaces. No foreign owner table is read by the migration.

Rollback is operational: stop writers and restore a verified database backup if full rollback is required. The schema Down operation always refuses to discard authoritative configuration; use a reviewed forward repair instead. Never run Down against the demo as acceptance cleanup.

## Scope

No CRM reseed, existing Deal stage changes, Quote/Order behavior, authentication changes, automatic migration on normal startup, or new production bypass. Acceptance may create clearly identified synthetic pipeline/Deal fixtures through authorized commands. Preserve the existing demo's 180 Deal states and versions; record before/after SQL evidence. Checkpoint A must read/write real SQL successfully before Frontend integration begins.
