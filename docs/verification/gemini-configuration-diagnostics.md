# Gemini configuration diagnostics

Latest source-only timeout review and final behavior: [Gemini timeout — code-only verification](gemini-timeout-code-only.md). This supersedes the earlier generic Test-budget mitigation below; the final 30-second default applies only to Gemini Configuration Test.

22/10/2026, Asia/Saigon. Source review and isolated regression verification only.

## Runtime findings

| Question | Result |
|---|---|
| Did the user's Save Draft succeed? | UNKNOWN: no connected admin browser or running UniCoreCRM API was available. |
| Did Set Credential succeed / is primaryCredentialConfigured true? | UNKNOWN for the user's Workspace. No credential was read or exported. |
| Did the failing Test reach Gemini? | UNKNOWN for the original failure. This investigation made zero live provider calls. |
| What did Gemini return? | UNKNOWN: no live response or incident correlation ID was available. |
| Is the key invalid? | NOT ESTABLISHED. A new key alone is not evidence of an authentication failure. |
| Do real Test and Activate pass? | BLOCKED_NO_CONNECTED_ADMIN_SESSION_AND_API. Mock transport results are not real-provider acceptance. |

Browser inventory contained no connected tabs. The listener/process inspection found no UniCoreCRM API or Vite listener (the launch profile uses API port 5080; frontend uses 3000). No backend was started against the user's database and no Workspace configuration was changed.

## Confirmed defects and likely incident explanation

**DERIVED_FACT — stale model choices:** the local Gemini catalog offered only `gemini-2.5-flash` and `gemini-2.5-pro`. Google's current [Gemini 2.5 Flash documentation](https://ai.google.dev/gemini-api/docs/models/gemini-2.5-flash) limits 2.5 access to prior users and directs new projects to current models. This is a plausible explanation if the new key belongs to a new/ineligible project, but the Workspace's actual provider response is still required to confirm it. Creating a new key in an existing project does not by itself prove this restriction applies.

**DERIVED_FACT — loss of failure detail:** the adapter classified errors by HTTP status alone and discarded Gemini's structured error code. A documented [HTTP 400 with ErrorInfo API_KEY_INVALID](https://ai.google.dev/gemini-api/docs/generate-content/api-errors) became InvalidRequest; the configuration application then collapsed it, model errors and network errors to generic AI_PROVIDER_UNAVAILABLE. HTTP 403 also incorrectly implied that the key itself was rejected, although it may indicate permission restrictions.

**DERIVED_FACT — unsaved key ambiguity:** typing a key did not save it, but Test could still run using the previously saved credential. The view showed no explicit server-recorded credential status.

## Source trace

1. `AiConfigurationView.tsx` awaits each mutation and replaces local configuration/version with the server response. Unsaved draft changes already blocked credential/Test/Activate in the working tree at task start.
2. Application services generate a new idempotency key per user action. `AiConfigurationHttpAdapter` forwards expectedVersion and uses `retry: never`; it does not swallow failed writes. Connected composition binds this adapter to the connected HTTP client. Chat's ConnectedAiRuntime does not own Studio configuration or provider credentials.
3. `AiConfigurationApplication.SaveAsync` admits provider/model/source before calling the store. `SetCredentialAsync` protects the credential with purpose `UnicoreCRM.WorkspaceAiCredential.v1`; successful store commits increment version. A successful Set Credential response, rather than typing in the field, establishes persistence.
4. `EfWorkspaceAiConfigurationStore.Project` sets primaryCredentialConfigured from the presence of protected storage. That flag does not prove successful decryption or provider authentication. Draft/credential writes reset IsValidated; changing providers clears that draft provider's credential.
5. Test checks expectedVersion, resolves the draft credential and calls each configured target once with synthetic instructions, context `{}`, and context count 0. It validates structured output before persisting validation against the same expectedVersion. No CRM records are loaded for this health check.
6. Activate requires the current version, IsValidated and readable credentials. The UI also requires a validated draft. No activation bypass was added.

## Changes

- Add stable `gemini-3.8-flash` to the allowlist and use it for never-configured Studio defaults. Google's [model page](https://ai.google.dev/gemini-api/docs/models/gemini-3.8-flash) documents structured output support. Retain 2.5 for eligible existing projects. Existing Workspace drafts/active configurations are not migrated.
- Decode at most 16 KiB of Gemini error JSON; retain only allowlisted error identifiers and numeric HTTP status. Never retain/render/log provider messages or metadata. Unknown/malformed/oversized errors fall back to HTTP classification.
- Distinguish missing versus unreadable stored credentials; replace broad exception swallowing with CryptographicException handling.
- Preserve authentication, permission, model/endpoint, request, account/billing prerequisites, network, quota/rate limit, timeout, safety and invalid-output distinctions. Keep the existing HTTP error codes/fieldErrors shape. HTTP 429 does not establish whether the exhausted limit is RPM, tokens, daily quota or spend; UI accurately groups quota/rate limits.
- Log safe Test failure classification/status/code with correlation ID. Log credential failures as occurring before the provider call. Do not log exceptions/inner exceptions, credentials, protected values, prompt bodies or provider response bodies.
- Show server-recorded key state, require saving newly entered keys before Test/Activate, explain legacy model access and provide bilingual recovery messages. Preserve transient network retry guidance without adding automatic retries to configuration testing.
- Treat malformed JSON envelope shapes as invalid provider responses rather than unhandled server errors.

## Verification

- PASS: `dotnet run --project scripts/AiProviderVerifier/UnicoreCRM.AI.ProviderVerifier.csproj` — mock HTTP 400 authentication vs malformed request/account prerequisite; 401/402/403/404/429/504/503; bounded/malformed error bodies; malformed success envelopes; missing/unreadable credentials with zero provider attempts; synthetic-only input; application error mapping; actual timeout cancellation; stale/pre-commit version conflict; failed tests cannot validate/activate; HTTP 200 with invalid structured output fails; safe log correlation; current model admission; existing adapter/retry/DataProtection regressions.
- PASS: ApiHost build (zero warnings/errors), frontend production build/bundle budget, AI application contracts, presentation responsibility, strict core/type regions, API management boundaries, frontend/backend separation, guidance contracts/runtime, mutation command authority and API contract.
- FAIL outside this change's source scope: full frontend `npm run verify` stops in lint/typecheck at `tests/contract/deal-quote-transition-outcome.test.mts:42` and `tests/e2e/studio-first-visit.spec.ts:50-51`.
- Other repository gates remain failing: backend readiness (two existing Studio design documents missing status entries); bilingual UI (8 literals in Communications/DealBuyerPicker/LeadQualification); quality pipeline (347 vs 345); repository inventory drift (includes existing file-count drift and changed source-line counts). No baseline or unrelated module was altered to conceal these failures.
- NOT RUN: authenticated connected/browser E2E, real Gemini Test, real Activate, production deployment. The scripted activation assertion uses a fake store and does not certify SQL persistence or real connectivity.
- Task source paths intersect zero files in the 103-file P0 pin reference. No SQL writes, authentication/authorization changes, staging, commits or pushes were performed.

## Next connected check

With the user's API and admin session available, read configuration flags/version only. For a new Google project choose Gemini 3.8 Flash, Save Draft, and confirm the returned draft/model/version. If a key is already recorded, keep it; otherwise the user enters/saves it in the password field. Run one synthetic Test, correlate its request ID with the safe backend log, then Activate only using the validated version returned by Test. Never request the key in chat or declare real-provider PASS from the isolated verifier.

## Timeout investigation

The user subsequently reported the Studio message corresponding to `AI_PROVIDER_TIMEOUT` with Gemini 3.8 Flash / Workspace / DRAFT. This is a reported symptom, not a captured HTTP result.

- Local tracked appsettings and Development settings both contain `AI:Provider:TimeoutSeconds = 10`. The optional Local file contains no timeout override; launch profiles contain no AI timeout override; the current execution environment contains no AI provider override. Local-file loading preserves environment-variable/command-line precedence. No running ApiHost was available, so the failing process's effective options and command-line overrides remain UNKNOWN.
- No matching `AI configuration test timed out` or `AI configuration test failed` entry was found in available `.log` files under backend/task-runtime. No incident correlation ID was available. Original request duration, whether response headers arrived, and successful credential decryption remain UNKNOWN.
- Browser inventory still contained no connected tabs; dotnet/node/API processes had zero listening sockets. No backend was started and no live provider calls were made. The Studio URL/correlation ID was requested without asking for keys or DevTools analysis.
- Confirmed transport-budget mismatch: FetchHttpClient defaults to 15 seconds; extending only the server's provider timeout would allow the browser to abort first.

Scoped mitigation and diagnostics (the incident cause is not yet proven):

- Preserve `AI:Provider:TimeoutSeconds` and Chat behavior. Introduce `AI:Provider:ConfigurationTestTimeoutSeconds` for synthetic Test only: default `max(30, clamped provider timeout)`, configurable within 1–60 seconds per target. With the checked-in 10-second provider value, the new Test default is 30 seconds. This is a bounded mitigation pending a live measurement, not evidence that a 30-second Test has passed.
- Extend only the `testAiConfiguration` browser operation to 130 seconds: two configured targets at the permitted 60-second maximum plus 10 seconds overhead. It remains one request with `retry: never`; caller/session cancellation is preserved. Other HTTP operation timeouts are unchanged.
- Mark server-created health-check requests explicitly. For Gemini 3.8 health checks only, send `generationConfig.thinkingConfig.thinkingLevel = low`, as documented in Google's [REST thinking guide](https://ai.google.dev/gemini-api/docs/generate-content/thinking.md). Chat, proactive requests and Gemini 2.5 do not receive this override.
- Safe logs now include effective Test/provider budgets, version, credential readability, elapsed milliseconds, application versus HttpClient deadline, provider HTTP 408/504, safe DNS/TLS/connection error enum, and caller cancellation. Gemini health-check transport logs distinguish waiting for headers from reading the body. Starting SendAsync does not prove that Gemini received a request; response headers provide evidence of an HTTP response.
- No exception, raw key, protected value, auth header or provider body is emitted by these diagnostics.

Regression PASS: independent/clamped Test budgets; Test can outlive the shorter Chat budget; thinking low only on Gemini 3.8 health checks; application/HttpClient/Google HTTP deadlines remain distinguishable; caller cancellation propagates; DNS/TLS/connection error enums are retained safely; Frontend Test budget is bounded and other requests/caller signals are preserved. These are isolated test-handler results, not real-provider acceptance.

Real Test, isValidated, Activate/ACTIVE and CRM Chat remain **BLOCKED_NO_CONNECTED_ADMIN_SESSION_AND_API**. No new runtime elapsed time or provider error code can be reported for the user's incident until that session/log is available. No P0 pinned path, baseline, authentication/authorization or TLS policy was changed; no staging/commit/push occurred.
