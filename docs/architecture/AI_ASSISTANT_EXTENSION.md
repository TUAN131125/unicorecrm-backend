# AI Assistant Extension

## Authority

`PROJECT_EXTENSION_AI_ASSISTANT`

The adopted frontend OpenAPI declares no AI operation, AI tag, or AI schema. The current command and query registries likewise contain no AI operation. The connected frontend names below are blocked proposals rather than an admitted wire contract. This backend-local extension is therefore introduced under the explicit project-extension authority granted for the AI Core task; it does not modify or claim provenance from the frontend OpenAPI.

## Operation matrix

| Operation | Proposed/current evidence | Classification | Owner | Effect | Decision |
|---|---|---|---|---|---|
| `askAiAssistant` | Frontend connected runtime only; `DEC-AI-ASSISTANT-API`; no OpenAPI path | BLOCKED | AI | advisory read | Do not implement the proposed contract |
| `listAiConversations` | Frontend connected runtime only; no OpenAPI path | BLOCKED | AI | read | Not implemented; no conversation persistence |
| `getAiConversation` | Frontend connected runtime only; no OpenAPI path | BLOCKED | AI | read | Not implemented |
| `createAiConversation` | Frontend connected runtime only; no OpenAPI path | BLOCKED | AI | AI-owned mutation | Not implemented |
| `appendAiConversationMessage` | Frontend connected runtime only; no OpenAPI path | BLOCKED | AI | AI-owned mutation | Not implemented |
| `deleteAiConversation` | Frontend connected runtime only; no OpenAPI path | BLOCKED | AI | AI-owned mutation | Not implemented |
| `evaluateAiActionGovernance` | Frontend connected runtime only; no OpenAPI path | BLOCKED | AI/AccessControl | decision | Not implemented; browser policy is not backend authority |
| `executeAiAction` | Frontend connected runtime only; no OpenAPI path | BLOCKED | owner/workflow dependent | business mutation | Not implemented |
| `createAiSuggestedTask` | WF-21 Work Activation source symbol; no OpenAPI path; workflow is blocked | BLOCKED | Workflows/Tasks | business mutation | Not implemented |
| `requestAiAdvisory` | Contract in this document | `PROJECT_EXTENSION_AI_ASSISTANT` | AI | advisory read | Implement |

The extension does not unblock or partially implement any blocked operation. In particular, an advisory suggested action is not an `executeAiAction` result and cannot create Task, Deal, or Lead state.

## Wire contract

- Operation ID: `requestAiAdvisory`.
- Method/path: `POST /ai/advisories`.
- Authentication: required B01 bearer session.
- Workspace: required B02 trusted Workspace resolved from `X-Workspace-Id`; a body value cannot select Workspace.
- Authorization: no canonical `ai.use` capability currently exists, so none is invented. Every requested owner context independently requires its canonical B03 capability: `leads.read`, `deals.read`, or `tasks.read`.
- Success: HTTP 200.
- Persistence: none. The request, prompt, and provider response are not stored.

The strict request object is:

```json
{
  "question": "What needs attention next?",
  "locale": "en",
  "contextReferences": {
    "leadId": "lead_...",
    "dealId": "deal_...",
    "taskId": "task_..."
  }
}
```

`question` is required after trimming and is limited to 2,000 characters. `locale` is optional and restricted to `en` or `vi`, defaulting to `en`. `contextReferences` is required and must contain at least one reference. At most one Lead, one Deal, and one Task can be requested. Unknown properties, including `workspaceId`, `memberId`, `actorId`, `roles`, `capabilities`, `systemPrompt`, `tools`, `providerUrl`, `apiKey`, `model`, and `modelInstruction`, are rejected by strict JSON deserialization and never influence execution.

The successful response is:

```json
{
  "executionId": "ai_exec_...",
  "summary": "...",
  "suggestedNextAction": "...",
  "attentionPoints": ["..."],
  "advisory": true,
  "contextReferences": {
    "leadId": "lead_...",
    "dealId": "deal_...",
    "taskId": "task_..."
  },
  "provider": {
    "name": "...",
    "model": "..."
  }
}
```

`executionId` is assigned by AI and is not a business aggregate, correlation, or provider request identifier. `summary` is required and bounded to 2,000 characters. `suggestedNextAction` is optional and bounded to 1,000 characters. At most five attention points are allowed, each bounded to 500 characters. Provider output is parsed as strict JSON and validated before this response is returned.

Stable extension errors are:

| Condition | Status/code |
|---|---|
| Malformed JSON or unknown property | 400 `AI_REQUEST_INVALID` |
| Body over 16,384 bytes | 413 `AI_REQUEST_TOO_LARGE` |
| Non-JSON content type | 415 `AI_UNSUPPORTED_MEDIA_TYPE` |
| Invalid semantic request or context-reference shape | 422 `AI_REQUEST_INVALID` |
| Missing owner read capability | 403 `AI_CONTEXT_ACCESS_DENIED` |
| Workspace context cannot be resolved | 403 `WORKSPACE_MISMATCH` |
| Foreign, absent, or record-scope-invisible context | 404 `AI_CONTEXT_NOT_FOUND` |
| No configured production-capable provider | 503 `AI_PROVIDER_UNAVAILABLE` |
| Provider exceeds the server timeout | 504 `AI_PROVIDER_TIMEOUT` |
| Provider returns malformed or invalid structured output | 502 `AI_PROVIDER_RESPONSE_INVALID` |

Errors never expose provider diagnostics, prompts, CRM content, SQL details, credentials, or hidden entity existence.

## Owner-approved context

AI composes context only through these owner-owned application contracts:

| Owner | Contract | Capability | Fixed projection |
|---|---|---|---|
| Leads | `ILeadSummaryReader` | `leads.read` | identifier plus visible `displayName`, work state, score, priority, and next-follow-up time |
| Deals | `IDealSummaryReader` | `deals.read` | identifier plus visible name, stage/category, opportunity score, expected close date, and next-action time/summary |
| Tasks | `ITaskSummaryReader` | `tasks.read` | identifier plus visible title, status, priority, and due time |

Each contract resolves the current trusted Workspace itself, validates the reference, applies the owner capability, scopes the query by Workspace, fails closed for unsupported restrictive record scopes, removes `HIDDEN` and `MASKED` fields before returning, exposes no EF entity, and records successful owner-read audit evidence. AI has no reference to an owner DbContext, repository, Infrastructure type, or SQL surface.

Context is bounded to three individual records. No list, search, full aggregate, activity history, notes, descriptions, buyer/contact details, assignee identity, credentials, authorization internals, or database metadata is sent to the provider.

## Prompt and tool boundary

Prompt composition keeps the system policy, user question, and CRM context data separate. CRM strings are serialized inside an explicitly delimited untrusted-data section. Text such as “ignore previous instructions” remains data and cannot change the trusted Workspace, tool registry, permissions, or selected records because those decisions are complete in code before provider invocation.

The allowlist contains only:

- `lead.summary.read` → Leads `ILeadSummaryReader`;
- `deal.summary.read` → Deals `IDealSummaryReader`;
- `task.summary.read` → Tasks `ITaskSummaryReader`.

All are bounded reads. The public request cannot name tools. The provider is not offered a tool-calling loop, and provider output cannot request a tool. There is no generic SQL, database, HTTP/API, browser, file, shell, code-execution, DI-service, or mutation tool.

## Provider model

`IAiProvider` is provider-neutral and accepts only the bounded prompt required by this operation. Provider selection, model identity, and timeout are server configuration. The provider call receives cancellation plus a finite timeout.

No external provider is currently authoritative or implemented. `DevelopmentDeterministic` is available only when the host environment is Development and it is explicitly selected through configuration. It supports deterministic normal, unavailable, timeout, and malformed-output verification modes; none of those modes is part of the public contract. Production and other environments fail closed with `AI_PROVIDER_UNAVAILABLE` until an admitted external adapter is configured. No API key is accepted by the request or committed in configuration.

## Advisory and unsupported behavior

Model output and the response are advisory only. A suggested next action is not a Task, does not carry a Task identifier, and is not persisted. No Lead is qualified or changed. No Deal stage, forecast, or next action is changed. No workflow—including WF-01, WF-09, WF-10, WF-13, WF-16, or WF-21—is invoked. Webhook processing does not trigger AI.

The architecture leaves a future approved mutation tool able to depend on a narrow owner application command without accessing owner persistence, but this extension admits no such tool.
