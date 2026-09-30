# CRM-PA-030 controller handoff

Implemented on-demand suggestions for an owned OPEN Customer attention item. No commit or push was made. This is the PA-030 implementation checkpoint, not completion of the CRM-PA roadmap.

## Source delta

| Property | Result |
| --- | --- |
| Canonical baseline | `e1de921dbcc6ea39cd2d879338a45cfb24fd96ef` |
| Current HEAD and master | `e1de921dbcc6ea39cd2d879338a45cfb24fd96ef` |
| Source files changed | 19 |
| Insertions / deletions | 760 / 5 |
| Artifact | `D:/Project_All/UnicoreCRM/backend/backend-work/review/pa030/CRM-PA-030-source-delta-e1de921.patch` |
| SHA-256 | `17e586a1ffd2a33cbd0cb05419f7b5f2513acca5fff78197bed8208511958b1d` |
| Reverse check against current source | PASS |
| Forward apply to complete baseline archive | PASS; every changed file matches its Git blob in the captured source delta |
| Actual reverse apply in isolated archive | PASS; entire baseline file/hash manifest restored exactly |
| Real repository index | Unchanged; source delta generated with a temporary index |

`delta-metadata.json` contains the complete changed-file list and isolated round-trip directory. `build-delta.py` reproduces packaging and both-direction validation. Pre-existing untracked plan/review material was preserved and is excluded from this source-only patch.

## Implementation

- One authenticated, trusted-Workspace endpoint: `POST /ai/proactive/items/{itemId}/suggestion`. Its only request field is locale; unrecognized fields are rejected. Invalid locale uses existing `AI_REQUEST_INVALID` / HTTP 422 semantics, with the existing English default.
- A separate suggestion application re-authorizes the capability, Workspace item, Customer subject, supported trigger, OPEN status, and item owner before requesting Customers-owned context.
- Customers owns current record authorization, current owner validation, field readability, and health assessment. The projection exports only CustomerId, safe DisplayLabel, readable CustomerStatus, HealthBand, ChurnRisk, HealthReasonCode, and HealthAlgorithmVersion. Unreadable status is omitted from serialized provider context. No optional context fields were needed.
- Deterministic Why uses the stored trigger/reason and current authorized Health band. Known risk reasons have vi/en text; unknown reasons have safe generic text. Provider Why is rejected.
- Dedicated prompt composer and strict output validator. Customer values remain serialized untrusted data. Output allows only summary, suggestedNextStep, and taskDraft title/description. Duplicate/unknown fields, invalid shapes, missing/blank text, malformed/oversized payloads are rejected without truncation. Tasks-compatible title/description maxima are 300/4000 characters.
- Existing `IAiProvider` production chain remains authoritative for Workspace provider/model, retry/fallback, guardrails, and attempts. The necessary shared-provider change adds an internal output-contract selector with Advisory as the unchanged default, and adapter-specific proactive response schemas. Neither provider resolution nor configuration ownership changed.
- REQUESTED audits are persisted before invocation. Each audit uses a fresh DI scope, isolating terminal writes from failed ledger changes. The execution ledger and safe audits correlate `ai_exec_{guid}`. Cancellation and ledger-write failures attempt failure audits with clean persistence state.
- Suggestions remain ephemeral. Only operational audit/execution evidence is persisted; business state is unchanged.

## Executed gates

All commands below ran from `D:/Project_All/UnicoreCRM/backend`. Final logs are in this directory.

| Gate | Result | Evidence |
| --- | --- | --- |
| Full solution build | PASS: 0 warnings, 0 errors | `build.log`; `dotnet build UnicoreCRM.slnx --no-restore -v:q` |
| Proactive scenario corpus | PASS: 107 checks, including 70 suggestion checks | `proactive.log`; `dotnet run --no-build --project scripts/ProactiveVerifier -v:q` |
| Proactive real SQL corpus | PASS: 227 total checks; includes 41 suggestion SQL checks and existing concurrency/security checks | `proactive-sql.log`; same verifier with isolated LocalDB connection for `UnicoreCRM_Proactive_Verifier_PA030_20260926` |
| AI Provider verifier | PASS, including Gemini/OpenAI proactive schemas and both Workspace-selected production paths | `provider.log`; `dotnet run --no-build --project scripts/AiProviderVerifier -v:q` |
| AI advisory/assistant | PASS | `advisory.log`; `./scripts/verify-ai-assistant.ps1 -DatabaseName UnicoreCRM_PA030_Advisory_20260926` |
| Customer Health | PASS: 29 checks | `health.log`; `dotnet run --project scripts/CustomerHealthVerifier -v:q` |
| Customers real ApiHost/security | PASS: 221 checks, 0 failures | `customers.log`; `./scripts/verify-customers-read-core.ps1 -DatabaseName UnicoreCRM_PA030_Customers_20260926 -Port 5336` |
| CommercialEvidence | PASS: 102 checks, 0 failures; architecture and pending-model checks also pass | `commercial.log`; `./scripts/verify-commercial-evidence-original-core.ps1 -DatabaseName UnicoreCRM_PA030_Commercial_20260926` |
| AccessControl real ApiHost/security | PASS: 565 checks, 0 failures | `access.log`; `./scripts/verify-access-control-record-access.ps1 -DatabaseName UnicoreCRM_PA030_Access_20260922 -Port 5319` |
| PlatformOperations pending models | PASS: AiExecutionDbContext, InboxDbContext, IntegrationEventJournalDbContext | `platform-ai.log`, `platform-inbox.log`, `platform-journal.log` |
| Customers pending model | PASS | `customers-model.log` and Customers corpus |
| AccessControl pending model | PASS | `access-model.log` and AccessControl corpus |
| CommercialEvidence pending model | PASS | `commercial.log` |
| Clean migration verification | PASS: all 21 ApiHost schema migrations applied to a freshly created isolated database; real SQL verifiers also migrate clean databases | First 45 lines of `customers.log`, Proactive and CommercialEvidence corpora |
| Accepted migration baseline update | PASS: no migrations applied; existing data hashes identical before/after | `upgrade.log`, `check-upgrade.ps1` |
| `git diff --check` | PASS for working tree and captured delta including new files | Packaging validation and final working-tree check |

Pending-model commands use `dotnet ef migrations has-pending-model-changes --project <owning-project> --context <context> --no-build`. No migration/model snapshot or persistence mapping changed from the canonical baseline. The accepted-baseline update is consequently a no-op, verified against existing Proactive items, audits, executions, and attempts; the data hash stayed `E875DE474493E64EDB5A4F586A75422F3CE893B8E58216839C000AFF8FEE36DE`.

No paid/live AI API call was used. Verifiers use deterministic adapters/transports and real isolated SQL/ApiHost where specified.

## Mandatory scenario evidence map

Numbers correspond to the 45 scenarios in the implementation request.

| Scenarios | Executable evidence |
| --- | --- |
| 1–4: own OPEN, structured response, deterministic Why, no provider Why authority | SuggestionVerifier success cases and strict validation; AiProviderVerifier schema tests; Customers HTTP vi/en cases |
| 5: other item owner | SuggestionVerifier negative case, zero calls/audits/executions |
| 6: current Customer reassigned | Real Customers/AccessControl HTTP case and unchanged generation execution count |
| 7: missing ai.proactive.use | SuggestionVerifier and real Customers HTTP capability removal |
| 8: missing customers.view | Real Customers HTTP capability removal and zero new generation executions |
| 9: Customer scope denied | Real Customers HTTP Team scope denial and zero new generation executions |
| 10–11: HIDDEN/MASKED health | Real Customers HTTP field-policy cases and zero new generation executions |
| 12–14: SNOOZED/DISMISSED/RESOLVED | SuggestionVerifier zero-call checks and real HTTP rejection cases |
| 15: foreign Workspace | SuggestionVerifier deliberately returns a foreign row to verify application rejection |
| 16: invalid locale | SuggestionVerifier zero-call check; real HTTP `422 / AI_REQUEST_INVALID` |
| 17: injection-looking values | Both exact requested strings are checked as serialized context values, absent from system/task instructions |
| 18–23: unknown/missing output fields and oversized title/description | Strict validator corpus, including duplicate keys and accepted 300/4000 boundaries |
| 24–28: provider failures leave Attention unchanged | SuggestionVerifier unavailable/timeout/rate-limit/refusal/malformed cases; real SQL unavailable/invalid output and real ApiHost unavailable/malformed |
| 29: successful item version unchanged | Exact item record comparisons in memory, SQL, and full real HTTP SQL snapshot |
| 30–31: Customer unchanged on success/failure | Full real Customer row JSON snapshots across success, unavailable, and malformed-provider HTTP requests |
| 32–33: zero Tasks created | Real Tasks table counts across successful and failed HTTP generation requests |
| 34: Workspace provider/model authority | AiProviderVerifier both configured production paths; SQL real configuration resolver and attempt model assertions |
| 35: no provider/model override | JSON contract tests and real HTTP provider/model/apiKey/credential/credentialRef rejection |
| 36: REQUESTED exists before provider | Probe provider checks; SQL adapter observes REQUESTED through a different DbContext; rejected audit test proves zero invocation |
| 37–38: terminal success/failure audits | In-memory, SQL, and real HTTP checks; dirty-ledger test asserts FAILED audit and no stale changes flushed |
| 39–40: execution and attempt evidence | SQL production provider/ledger checks, correlated execution IDs; provider verifier |
| 41–43: no raw output/context/credentials in audit/ledger/logging | SQL serialized audit/ledger checks for fixture context/output/credential; production logging reviewed to contain only operation metadata; existing provider and advisory security regressions |
| 44–45: vi/en response contracts | In-memory formatter checks and real HTTP localized deterministic-provider responses |

Additional checks cover cancellation evidence, supported subject/trigger, strict duplicate JSON rejection, bounded payloads, and clean terminal persistence after a real SQL write failure.

## Architectural confirmation

Confirmed against the complete captured source delta and production source:

- No Task mutation or Task creation; no TasksDbContext or Tasks create participant in the suggestion path.
- No Customer, CommercialEvidence, workflow, email, or generic tool mutation.
- No frontend changes; frontend working tree remains clean.
- No new production project, verifier project, project reference, or DbContext.
- No migration, schema change, pending EF model change, or suggestion/history/draft table.
- No direct CustomersDbContext or CommercialEvidence access from AI.
- No provider invocation in the proactive worker/evaluator; generation is registered only behind the explicit authenticated HTTP request.
- No generic rules DSL, mutation framework, autonomous agent, provider selection request field, or durable response replay contract.
- No commit, push, or roadmap completion declaration.

CRM-PA-030 AI SUGGESTION READY FOR CONTROLLER REVIEW
