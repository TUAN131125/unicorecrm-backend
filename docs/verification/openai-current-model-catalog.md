# OpenAI current model catalog — 2026-10-22

Status: focused code verification PASS; repository-wide verification NOT_GREEN; REAL_PROVIDER_TEST = NOT_RUN.

## Findings and decisions

1. The previous production OpenAI catalog offered `gpt-5-mini` and `gpt-5`; the unspecified OpenAI deployment model also defaulted to `gpt-5-mini`. This is a source finding, not a query of a live Workspace. Explicit legacy references remain in compatibility fixtures and connected-test scenarios.
2. The Backend now offers these exact API IDs in this order: `gpt-6-luna`, `gpt-6.1-sol`, `gpt-6-astra`, `gpt-5.6-sol`, `gpt-6-sol`, `gpt-5.6-terra`, `gpt-5.6-luna`. Each individual official model page documents Responses and structured output support. Catalog admission does not establish access for a particular API account.
3. `gpt-6-luna` is first when an administrator switches to OpenAI in Studio, and is the fallback model value for an explicitly OpenAI deployment default whose model is unspecified. Its documented lower cost and structured output support fit routine CRM work. The global default provider remains unchanged; explicit deployment settings and stored Workspace policies take precedence.
4. The adapter already used `POST https://api.openai.com/v1/responses`; that endpoint is retained. New-model requests explicitly use `reasoning.effort=medium` for Chat and `low` for synthetic Configuration Test. No `none`, temperature, top_p, tools or streaming parameter is added. Legacy-model requests omit reasoning, preserving previous behavior.
5. Strict `text.format` JSON schema still requires `summary`, `attentionPoints` and nullable `suggestedNextAction`. The existing output validator remains mandatory. `store=false` and `max_output_tokens=2048` are retained. This budget includes reasoning tokens; sufficient output for every workload is not established. Incomplete output fails validation instead of activating.
6. The previous parser returned only the first text part and did not require an overall completed status. It now combines text parts, skips reasoning items, detects refusal, rejects malformed/empty/incomplete/failed responses, and safely reads optional usage/ID fields. HTTP success status is retained for malformed response diagnostics. HttpClient timeout, caller cancellation and network/read failures remain distinguishable. No timeout increase or new retry was introduced.
7. Studio obtains selectable IDs, labels and order from the Backend catalog. The frontend owns only VI/EN purpose copy. A saved model outside the current catalog retains its selected value and receives an update notice; refresh does not silently choose a replacement. Gemini selectors keep their previous behavior.
8. `gpt-5` and `gpt-5-mini` remain admitted for compatibility but are hidden from the new-model catalog. This also means a direct API caller can still explicitly save those IDs. Official deprecations currently list the underlying 2025-08-07 snapshots for shutdown on 2026-12-11, which is after this report date. No already-completed shutdown is inferred. Actual account/model availability still requires a provider response.
9. Stored Active policies, model IDs and credentials are not migrated. Changing only the model retains the protected credential in the existing EF store; provider changes retain the existing credential-reset rules. Saving a draft/rotating a key invalidates draft validation. Failed Test cannot Activate; successful Test must use its returned latest version for Activate. Existing explicit fallback policy is unchanged.
10. No real OpenAI Test, Activate or Lead/Customer Chat was performed. No safely supplied OpenAI credential was available. No local service was started and no desktop/browser window was operated. Mock success is not real-provider acceptance.

## Files changed for this task

Backend:

- `src/UnicoreCRM.AI/Providers/AiProviderCatalog.cs` — current catalog/default and legacy compatibility.
- `src/UnicoreCRM.AI/Providers/WorkspaceAiProviderResolver.cs` — unspecified OpenAI deployment model default only.
- `src/UnicoreCRM.AI/Providers/OpenAiProvider.cs` — reasoning request compatibility, response parsing and safe failure classification.
- `scripts/AiProviderVerifier/OpenAiModelVerifier.cs` — focused mock contracts and command-boundary regression checks.
- `scripts/AiProviderVerifier/Program.cs` — invoke focused verifier, recording-store hooks and optional live OpenAI target default. The live branch was not run.
- This verification report.

Frontend paths relative to `frontend/unicorecrm-web`:

- `src/workspaces/studio/presentation/components/AiModelSelect.tsx` — server-driven selector, legacy selected option and translated purpose copy.
- `src/workspaces/studio/presentation/views/AiConfigurationView.tsx` — use selector for OpenAI primary/fallback only.
- `src/guidance/content/studio/configuration.ts` — AI guidance copy/version only; unrelated existing changes preserved.
- `tests/quality/unit/check-ai-application-contracts.mts` — rendered selector behavior, translated purpose, legacy preservation and existing gates.

ProvidersModule, AiConfigurationApplication, credential store/DataProtection, output validator, authorization and AI Chat application code were reviewed but not changed by this task. Gemini adapter and its existing changes were preserved. Generated API contract and accepted compatibility baseline were not edited. All 103 pinned file hashes match the task-start snapshot. No stage, commit or push.

## Verification evidence

| Check | Result |
| --- | --- |
| `dotnet build src/UnicoreCRM.ApiHost/UnicoreCRM.ApiHost.csproj --no-restore` | PASS, 0 warnings/errors |
| `dotnet run --project scripts/AiProviderVerifier/UnicoreCRM.AI.ProviderVerifier.csproj --no-restore` | PASS: GEMINI_REST_CONTRACT_MOCK_PASS, OPENAI_CURRENT_MODELS_MOCK_PASS, AI_PROVIDER_ADAPTER_VERIFIER_PASS |
| Frontend `npm run typecheck` | PASS, all 3 configured gates |
| Frontend `npm run build` | PASS, bundle budget PASS; Vite advisory about large chunks |
| `quality.ai-application-contracts` | PASS, including existing scoped Lead/Customer context, connected runtime, provider isolation and mutation contracts |
| `quality.presentation-responsibility`, `quality.mutation-command-authority`, `quality.guidance-contracts` | PASS |
| `quality.api-management-boundaries`, `quality.frontend-backend-separation`, `quality.api-contract` | PASS; API contract checksum unchanged |
| `quality.strict-core`, `quality.strict-type-regions` | PASS |
| Frontend `npm run verify` | FAIL at lint; remaining full-pipeline gates did not run |
| `quality.backend-readiness`, `quality.quality-pipeline`, `quality.repository-inventory` | FAIL, details below |
| Real provider Test / Activate / CRM Chat | NOT_RUN |

New mock coverage: all seven exact model IDs; unknown/provider-mismatched admission; legacy read/active resolution; unspecified versus explicit deployment default; exact Responses path/model/reasoning/schema; reasoning plus multiple text parts; malformed JSON/envelope, empty/invalid contract and incomplete/failed output; HTTP 400/401/403/404/429/500/503/504; HttpClient deadline versus caller cancellation; no credential/raw error body leakage; failed Test blocks Activate; stale Test is rejected before HTTP; Activate requires the latest validated version; no adapter retry. Recording-store tests also verify protection of a newly supplied synthetic credential, trusted Workspace/version, no retry or credential replacement on rotation conflict, and Disable command/result mapping. Existing verifier regressions exercise inactive Workspace blocking, scoped resolution, credential unreadability, cancellation, fallback and Gemini REST contracts.

Database persistence, successful real credential rotation, real Disable/Activate, browser interaction and real Lead/Customer Chat are not established by these mocks. EF credential/model/active-policy behavior was reviewed read-only, not exercised against a database.

Repository-wide failures were retained rather than changing unrelated files or baselines:

- Full verify lint: `tests/contract/deal-quote-transition-outcome.test.mts:42` and `:148` — model possibly null; `tests/e2e/studio-first-visit.spec.ts:50` and `:51` — string or undefined passed to string. These are outside this task's changed files.
- Backend-readiness: the existing `docs/design/studio-connections-api/design.md` and `source-and-file-plan.md` lack document status entries.
- Quality-pipeline: expected count 345 versus actual 347.
- Repository-inventory: checked-in inventory does not match the current dirty tree. The new selector also adds a source file. Inventory/baseline regeneration was not folded into this task.

## Official sources checked

- [Models](https://developers.openai.com/api/docs/models), [latest model guide](https://developers.openai.com/api/docs/guides/latest-model), [reasoning guide](https://developers.openai.com/api/docs/guides/reasoning), [deprecations](https://developers.openai.com/api/docs/deprecations).
- Individual API model pages: [GPT-6 Luna](https://developers.openai.com/api/docs/models/gpt-6-luna), [GPT-6 Sol](https://developers.openai.com/api/docs/models/gpt-6-sol), [GPT-6.1 Sol](https://developers.openai.com/api/docs/models/gpt-6.1-sol), [GPT-6 Astra](https://developers.openai.com/api/docs/models/gpt-6-astra), [GPT-5.6 Luna](https://developers.openai.com/api/docs/models/gpt-5.6-luna), [GPT-5.6 Terra](https://developers.openai.com/api/docs/models/gpt-5.6-terra), [GPT-5.6 Sol](https://developers.openai.com/api/docs/models/gpt-5.6-sol).

## Runtime acceptance after local is available

Use the authenticated Studio session and Backend catalog in the intended Workspace. Preserve its current Active configuration. Explicitly choose a new OpenAI model, save the draft and retain the credential if its configured source is valid. Enter a credential only through the normal protected UI if missing. Run one synthetic Configuration Test; inspect safe status/application code/correlation diagnostics, without exporting credentials, headers or provider bodies. Only after `isValidated=true`, Activate using the returned latest version, confirm ACTIVE through Backend GET and browser refresh, then check authorized Lead and Customer Demo Chat. Account access, quota, live output budget and latency remain runtime questions.
