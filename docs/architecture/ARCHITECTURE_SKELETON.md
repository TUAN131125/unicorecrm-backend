# UnicoreCRM Backend Architecture Skeleton

## Status and scope

B00 establishes the backend architecture and compilable physical skeleton only. It introduces no business feature, public endpoint, persistence model, workflow implementation, provider, webhook, authentication, workspace, permission, AI, or background-processing behavior.

No authoritative repository target framework was found. B00 therefore selects `.NET 10` (`net10.0`) as an implementation choice because the installed SDK is 10.0.400 and .NET 10 is an active LTS release. The support status was checked against the [official .NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy).

## Architecture style

The backend is a bounded-context modular monolith with:

- DDD business ownership;
- Clean Architecture dependency direction inside each canonical owner;
- module-first, layer-second organization;
- Vertical Slice organization inside future Application features;
- CQRS-lite without separate read/write infrastructure by default;
- explicit, authority-approved cross-owner contracts.

Canonical Owner, Bounded Context, and Physical Assembly are different concepts. The first deployment is one modular-monolith application and the initial persistence topology is one relational SQL database/server. B00 introduces neither microservices nor distributed-system infrastructure.

## Physical topology

```text
backend/
├── UnicoreCRM.slnx
├── Directory.Build.props
├── Directory.Packages.props
├── docs/architecture/
└── src/
    ├── UnicoreCRM.ApiHost/                 executable composition root
    ├── UnicoreCRM.BuildingBlocks/          owner-neutral technical primitives only
    ├── UnicoreCRM.Platform/                bounded context assembly
    ├── UnicoreCRM.Crm/                     bounded context assembly
    ├── UnicoreCRM.Sales/                   bounded context assembly
    ├── UnicoreCRM.Billing/                 bounded context assembly
    ├── UnicoreCRM.Fulfillment/             bounded context assembly
    ├── UnicoreCRM.Operations/              bounded context assembly
    ├── UnicoreCRM.CommercialEvidence/      bounded context assembly
    ├── UnicoreCRM.Workflows/               bounded context assembly
    ├── UnicoreCRM.Integrations/            bounded context assembly
    ├── UnicoreCRM.AI/                      bounded context assembly
    └── UnicoreCRM.PlatformOperations/      bounded context assembly
```

There is one executable: `UnicoreCRM.ApiHost`. No per-owner Domain/Application/Infrastructure projects and no test projects are created in B00.

## Owner and context map

| Logical bounded context | Canonical owners/capabilities | Initial physical assembly |
|---|---|---|
| Platform | IdentityAuth, Workspace, AccessControl, WorkspaceConfig **[DEFERRED]** | `UnicoreCRM.Platform` |
| WorkspaceStudio | Studio **[DEFERRED]** | None; physical assembly creation is deferred |
| CRM | Leads, Deals, Contacts, Customers, Organizations | `UnicoreCRM.Crm` |
| Sales | Products, Quotes, Orders | `UnicoreCRM.Sales` |
| Billing | Invoices, Payments | `UnicoreCRM.Billing` |
| Fulfillment | Shipping, Returns | `UnicoreCRM.Fulfillment` |
| Operations | Tasks, Support | `UnicoreCRM.Operations` |
| CommercialEvidence | CommercialEvidence | `UnicoreCRM.CommercialEvidence` |
| Workflows | Atomic, Durable | `UnicoreCRM.Workflows` |
| Integrations | Webhooks/Inbound, Webhooks/Outbound, Providers | `UnicoreCRM.Integrations` |
| AI | Gateway, Context, Prompts, Tools, Providers, Usage | `UnicoreCRM.AI` |
| PlatformOperations | Outbox, Inbox, Idempotency, BackgroundJobs, RuntimeState | `UnicoreCRM.PlatformOperations` |

`DEFERRED` or `BLOCKED` does not erase canonical ownership. WorkspaceConfig and WorkspaceStudio/Studio are part of the logical architecture now, but their current mutation semantics are not admitted for implementation and must not be invented. Logical ownership does not require an immediate physical assembly; the initial topology intentionally defers a dedicated WorkspaceStudio project. Neither deferred capability is added to the B01-B09 implementation order.

## Module structure

Each bounded-context assembly is organized by canonical owner first:

```text
UnicoreCRM.Crm/
└── Leads/
    ├── Domain/
    ├── Application/
    ├── Infrastructure/
    ├── Contracts/
    └── LeadsModule.cs
```

Future application features use a vertical slice beneath the owner:

```text
Leads/Application/CreateLead/
├── Command.cs
├── Handler.cs
├── Validator.cs
└── Result.cs
```

B00 creates no fake feature slices and no giant `*Service.cs` classes. Owner module files are no-op internal composition placeholders. Only the bounded-context registration surface is public to ApiHost.

## Dependency rules

The physical project-reference graph is:

```text
UnicoreCRM.ApiHost
├── UnicoreCRM.Platform ───────────────┐
├── UnicoreCRM.Crm ────────────────────┤
├── UnicoreCRM.Sales ──────────────────┤
├── UnicoreCRM.Billing ────────────────┤
├── UnicoreCRM.Fulfillment ────────────┤
├── UnicoreCRM.Operations ─────────────┤
├── UnicoreCRM.CommercialEvidence ─────┤
├── UnicoreCRM.Workflows ──────────────┤
├── UnicoreCRM.Integrations ───────────┤
├── UnicoreCRM.AI ─────────────────────┤
└── UnicoreCRM.PlatformOperations ─────┤
                                        └── UnicoreCRM.BuildingBlocks
```

Rules:

- ApiHost owns composition and may reference bounded-context assemblies.
- A bounded-context assembly may reference BuildingBlocks.
- Cross-bounded-context project references are default-deny, not universally forbidden. A directed reference may exist only when a real admitted consumer must compile against an explicitly approved public contract or application boundary.
- Cross-owner communication must use an explicit approved contract. A physical project reference does not grant the consumer business ownership.
- A consumer must not access the producer's Domain implementation, Infrastructure, DbContext, repository, EF entity, migration, or internal implementation types.
- Cross-bounded-context references must remain directional; circular project references are forbidden.
- If an owner's Contracts surface gains significant foreign consumers, or stronger compile-time isolation becomes valuable, it may later be promoted to a dedicated `UnicoreCRM.<Owner>.Contracts` assembly. B00 creates no separate Contracts projects.
- The current graph contains no cross-bounded-context reference because no admitted consumer currently requires one. No artificial reference may be added merely to demonstrate conditional allowance.
- Domain depends on neither Application nor Infrastructure.
- Application may depend on its owner's Domain and explicitly approved Contracts, including an approved foreign owner contract when a real consumer requires it.
- Infrastructure implements its owner's application ports.
- Contracts are explicit and narrow; an approved contract does not expose Domain implementation, Infrastructure, or persistence.
- Cross-owner access is default-deny.
- BuildingBlocks may contain technical primitives only. It must never contain owner-specific business concepts.
- No speculative `IRepository<T>`, universal unit of work, event bus, saga engine, distributed lock, or cache abstraction is admitted.

## Data ownership

One SQL database/server is a deployment choice, not shared business ownership. Every canonical owner owns its own state. Future logical schemas may include:

`iam.*`, `workspace.*`, `access.*`, `leads.*`, `deals.*`, `contacts.*`, `customers.*`, `organizations.*`, `products.*`, `quotes.*`, `orders.*`, `invoices.*`, `payments.*`, `shipping.*`, `returns.*`, `tasks.*`, `support.*`, `commercial_evidence.*`, `workflow.*`, `integration.*`, `ai.*`, and `ops.*`.

B00 creates none of these schemas or tables. Future persistence may use owner-specific contexts such as `LeadsDbContext`, `DealsDbContext`, or `TasksDbContext`. A giant shared `CrmDbContext` is forbidden as a cross-owner gateway. No owner may access another owner's DbContext, repository, Infrastructure, EF entity, table, or migration.

## Cross-owner behavior

- Approved reads may use explicit approved Contracts.
- A single delegated mutation may use an approved owner contract only when authority proves it.
- Multi-owner business mutation belongs to Workflows.
- A business module must not orchestrate mutation across multiple foreign owners.

## Workflow model

`Atomic/` is reserved for authoritative multi-owner mutations that must commit or roll back together and can use one local database transaction.

`Durable/` is reserved for authoritative multi-owner work where retry, timeout, progress, or compensation has business meaning and completion cannot occur in one local transaction.

B00 implements no workflows and introduces no Saga engine.

## Integrations boundary

Future inbound flow:

```text
External Provider
→ verification
→ Inbox
→ dedupe
→ mapping/normalization
→ canonical business command
→ authoritative owner
```

Integrations is a first-class boundary. A webhook must never write CRM persistence directly. B00 implements no webhook or provider behavior.

## AI boundary

AI is a first-class boundary but never business authority. It may read approved context, generate suggestions, and request approved tools or commands. It may not access business DbContexts or repositories, fabricate permissions or aggregate identity, or directly mutate business state. B00 implements no AI provider.

## Foundation request pipeline

The future request authority flow is:

```text
HTTP
→ Authenticate User
→ Resolve Requested Workspace
→ Verify Workspace Membership
→ Trusted CurrentWorkspace
→ Authorize Permission
→ Application Use Case
```

A workspace request/header value is not authority by itself. Permission enforcement must be possible at the application boundary, not only through HTTP attributes.

## Aggregate identity

Aggregate identity belongs to its canonical owner. Server-assigned IDs must not be fabricated by the frontend, AI, webhook, workflow, or foreign modules. IntentKey, DedupeKey, IdempotencyKey, and CorrelationId are not aggregate IDs.

Task identity is server-assigned. Synthetic `task_deal_*` or similar intent/dedupe keys must not become Task IDs without explicit current canonical authority.

## Frozen architecture laws

- **LAW-01** Module first, layer second.
- **LAW-02** Canonical Owner != Bounded Context != Physical Assembly.
- **LAW-03** Each canonical owner owns its business state.
- **LAW-04** No foreign DbContext access.
- **LAW-05** No foreign Infrastructure access.
- **LAW-06** Multi-owner business mutation belongs to Workflow.
- **LAW-07** HTTP/Webhook/AI do not directly write business persistence outside the authoritative application boundary.
- **LAW-08** Aggregate identity belongs to the aggregate owner.
- **LAW-09** Workspace authority requires authenticated user + verified membership.
- **LAW-10** Permission enforcement belongs at the application boundary.
- **LAW-11** BuildingBlocks contains technical primitives only.
- **LAW-12** Never invent a missing business contract.
