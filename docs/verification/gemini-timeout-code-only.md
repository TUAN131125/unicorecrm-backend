# Gemini timeout — code-only verification

22/10/2026, Asia/Saigon. The user deliberately stopped local services and requested source review and isolated code tests only. No port/browser inspection, service startup, live provider request, SQL access, migration, seed, staging, commit or push was performed in this task.

## Verified causes in source

1. The original Configuration Test reused the 10-second provider deadline. Checked-in base/Development settings both specify 10 seconds; local overrides and environment/command-line values retain precedence. This proves a locally imposed early-cutoff path, not the cause of the user's specific incident. The earlier mitigation separated a 30-second Test budget but also applied it to OpenAI; the final code confines it to Gemini Test.
2. The frontend's default 15-second transport deadline could cancel a Test before a raised server deadline. The existing operation-specific 130-second Test wrapper is retained and tested; unrelated calls keep their original budget. It covers at most two targets at 60 seconds each plus overhead. It does not add retries.
3. Cancellation without evidence of a deadline was previously reported as provider timeout. Gemini now labels HttpClient timeout only when the exception carries TimeoutException and the request is a configuration test. Application deadline, caller cancellation, network error and unexplained cancellation remain distinguishable.
4. Reading a stalled error body could hide conclusive HTTP 401/403/429/5xx headers behind a later timeout. These failures are now classified immediately from headers; HTTP 400 alone needs bounded ErrorInfo parsing to distinguish key rejection, request format and account prerequisite failures.
5. A failed re-test could leave a previous IsValidated=true in storage. Failures now revoke validation only for the tested version. A concurrent newer configuration is protected by optimistic concurrency. The UI reloads safe metadata after Test failure, shows loading on Test itself and has an immediate in-flight lock against duplicate submissions. Browser transport timeout/cancellation receive separate safe messages. If the metadata reload fails, the UI explicitly asks for a page refresh; Backend remains the activation authority.
6. CancellationTokenSource alone relies on transport cooperation. The application also bounds its await with the same cancellation token; an unresponsive/non-cooperative adapter cannot keep the Test awaiting forever. The token is still passed to HttpClient and response-body parsing; no additional provider attempt is created.

## Hypotheses deliberately left unconfirmed

The original request's actual runtime overrides, elapsed time, receipt by Google, network/DNS/TLS conditions, model latency, provider HTTP response and stored credential readability were not measured. The message alone does not establish that Google was slow or that the API key was invalid. Real Test/Activate/CRM Chat acceptance is intentionally deferred to a separate user-started local session.

## Final timeout behavior

| Layer | Original | Final with checked-in defaults |
|---|---|---|
| Gemini Configuration Test | 10 seconds per target | 30 seconds per Gemini target |
| OpenAI Configuration Test | 10 seconds per target | 10 seconds, unchanged |
| AI Chat provider attempt | 10 seconds | 10 seconds, unchanged |
| Typed HttpClient | Finite 100-second default | Unchanged; verified above the 60-second application cap |
| Frontend Configuration Test | Inherited 15 seconds | Dedicated 130-second finite upper bound |
| Other frontend HTTP calls | Existing timeout | Unchanged |

`AI:Provider:TimeoutSeconds` stays clamped to 1–60 seconds. Optional `AI:Provider:ConfigurationTestTimeoutSeconds` is used only for Gemini Test and is also clamped to 1–60; its default is max(30, the clamped provider timeout). OpenAI Test and Chat use TimeoutSeconds. Existing one-argument verifier options retain their explicit fixture timeout.

No environment change is required. An optional deployment/development override is `AI__Provider__ConfigurationTestTimeoutSeconds=30`; a deliberate lower override is respected. Values are resolved at service registration, so a running process needs a normal restart after source/config changes.

The deadline covers sending, waiting for headers and reading/parsing the provider body. Credential resolution (storage read plus DataProtection unprotect) is measured separately and precedes that provider deadline. Application elapsed logs begin after authorization/configuration read; they are not claimed to represent total HTTP latency. No API request-timeout middleware or API reverse-proxy deadline was found in the reviewed ApiHost, Vite config or checked-in static nginx config; infrastructure outside the repository is unverified.

## Request, cancellation and persistence

- Use the existing POST `v1beta/models/{model}:generateContent`, server-owned x-goog-api-key header, synthetic `{}` context, zero CRM context records and unchanged JSON schema/output validation.
- Only server-marked Gemini 3.8 synthetic health checks add `generationConfig.thinkingConfig.thinkingLevel = low`. Chat, proactive generation and Gemini 2.5 do not receive this field. See Google's [GenerateContent thinking REST contract](https://ai.google.dev/gemini-api/docs/generate-content/thinking.md).
- Pre-cancelled callers start no provider attempt; cancellation during the request propagates and is not converted to provider timeout. A timeout or other failed Test never calls Activate automatically.
- Failed Test does not write a credential or mark validation true. Previously valid drafts are invalidated using expectedVersion; a concurrent version conflict returns AI_CONFIGURATION_VERSION_CONFLICT. The active snapshot, if any, remains governed by existing persistence policy.
- Stable public distinctions: AI_PROVIDER_TIMEOUT, AI_PROVIDER_RATE_LIMITED, AI_PROVIDER_RESPONSE_INVALID, AI_CONFIGURATION_VERSION_CONFLICT; safe AI_PROVIDER_UNAVAILABLE reasons distinguish credential, permission, model, network and unexplained cancellation. Unknown cancellation uses PROVIDER_REQUEST_CANCELLED, not a guessed deadline.
- Correlation, elapsed milliseconds, effective budgets, safe network enums, provider HTTP status, response phase and credential-readable boolean may be logged. Keys, decrypted/protected values, auth headers, CRM content, exception objects and raw provider bodies are not logged by these diagnostics.

## Focused verification

`dotnet run --project scripts/AiProviderVerifier/UnicoreCRM.AI.ProviderVerifier.csproj`: **PASS**. Default mode uses stub transports/fixtures; the `live` mode was not invoked.

| Required case | Proof | Result |
|---|---|---|
| Valid structured output within budget | Fake clock: reply at 11s succeeds with Gemini Test 30s / Chat 10s | PASS |
| Response later than deadline | Advance fake clock to 30s; exact elapsed=30000, no validation | PASS |
| Caller cancellation | Explicit cancellation and pre-cancelled request; no false timeout / zero pre-cancel attempts | PASS |
| Provider never responds | Blocked headers, blocked 200 body and transport ignoring cancellation; finite await | PASS |
| HTTP 401/403 | Authentication/permission distinction, including unreadable stalled error body | PASS |
| HTTP 429 | Rate/quota category preserved without retry | PASS |
| HTTP 5xx | 500/503 unavailable versus 504 provider HTTP deadline | PASS |
| Network | Safe NameResolutionError / SecureConnectionError / ConnectionError classifications | PASS |
| Invalid JSON | Malformed provider success JSON / unexpected envelope shapes | PASS |
| Invalid structured output | HTTP 200 missing required output; zero validation writes | PASS |
| Credential missing/unreadable | Zero provider calls; distinct missing/decryption reasons | PASS |
| Version conflict after response | Success cannot validate stale version; failed re-test cannot invalidate newer version | PASS |
| Failed Test cannot Activate | Fresh draft and previously validated draft both fail policy gate after failed Test | PASS |
| Successful Test allows Activate | Correct validated version passes application policy with fake store | PASS |
| OpenAI and Chat regression | Existing adapter/schema/orchestration suite, unchanged budgets and no health-check thinking override in Chat | PASS |

Fake-clock deadline tests advance time without Thread.Sleep or real 30-second waits. Short pre-existing orchestration/HttpClient timer tests and 5-second broken-fixture guards remain finite. No SQL persistence or real-provider PASS is claimed by the fake-store activation assertion.

Frontend AI application contracts, strict core/type regions, presentation responsibility, Studio configuration experience and guidance contracts/runtime: **PASS**. ApiHost build: **PASS**, zero warnings/errors. Frontend production build/bundle budget: **PASS**. UI loading/locking/reload behavior was reviewed in source and protected by the existing source-contract checks; no browser/component E2E PASS is claimed. Full repository verification was not rerun as a governance/architecture campaign; unrelated failures previously recorded in the broader diagnostics report are not waived or claimed fixed.

## Changed files

Backend source/tests in this code-only revision:

- `src/UnicoreCRM.AI/Providers/AiProvider.cs`
- `src/UnicoreCRM.AI/Providers/ProvidersModule.cs`
- `src/UnicoreCRM.AI/Providers/GeminiAiProvider.cs`
- `src/UnicoreCRM.AI/Providers/ProductionAiProviderAdapter.cs`
- `src/UnicoreCRM.AI/Gateway/AiConfigurationApplication.cs`
- `scripts/AiProviderVerifier/Program.cs`

Frontend source/tests/guidance:

- `src/ai/application/aiProviderFailure.ts`
- `src/workspaces/studio/presentation/views/AiConfigurationView.tsx`
- `tests/quality/unit/check-ai-application-contracts.mts`
- `src/guidance/content/studio/configuration.ts`

The earlier Test HTTP budget wrapper in AiConfigurationHttpAdapter.ts and its composition wiring are retained and covered by regression tests. Other pre-existing dirty work was preserved. All **103 P0 pinned files have identical before/after SHA-256** for this code-only revision.

## When local is enabled again

Start local normally from the current working tree and reload Studio to receive the new frontend bundle. Keep Gemini 3.8 Flash / Workspace and the already saved key; a saved credential flag does not prove provider authentication. Save any draft edits, run one synthetic Test, and Activate only after success using the version returned by Backend. A failure now leaves the draft unvalidated and provides safe timing/failure evidence. Real Gemini Test, Activate and CRM Chat are reserved for the separate acceptance run requested by the user.
