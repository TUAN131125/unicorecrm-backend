# B6.3B — Manual source workflow decision

User task 3eee319a plus explicit follow-up authorize **Development simulation only**. No operational source has been approved. Actual-source verification remains BLOCKED_CONTRACT; no VERIFIED/ACCEPTED/WON/Gate C.

- Reuse existing Case/proof storage, source registry, immutable version ledger and capabilities. No new provider/storage/module or scanner policy change.
- Admit only ORGANIZATION_ISSUED_AUTHORITY_WITH_INDEPENDENT_SOURCE_CHECK, for an Organization Buyer with explicit active Contact affiliation. Source issuer ID equals Buyer organization.
- Workspace Backend config must explicitly enable ManualDevelopmentSimulation and host must be Development. Policy digest includes documentary policy version/freshness/limits. There is no operational enabling path.
- Source evidence binds exact Case/Quote/document/revision/hash/commercial content, Subject/CRM versions, scope, issuer proof and separate independently established channel proof. Source creator cannot approve; approver also differs from recorder/Quote creator/requester and must hold ManageSources + protected content access. APPROVED is explicitly DEVELOPMENT_SIMULATION. Existing descriptors remain unchanged.
- Checker's trusted identity differs from source creator/approver, recorder, Quote creator/requester. Structured observation needs distinct CLEAN confirmation evidence, the established independent channel, matching scope and observation timestamp. Free text/URL/email/checkbox alone cannot produce a successful result.
- Freshness starts at observedAt, capped at 24 hours and honoring narrower Workspace policy. Reusing confirmation evidence cannot renew an observation; same-key replay is authorized again and returns a fresh current projection with the immutable original receipt identity/version.
- Current READY requires all proof/source/subject/version/policy bindings, validity, value/currency, B1 approval, B2 delivery, B5 bytes/hash and affiliation. Root acceptance verification stays blocked. Simulation readiness cannot authorize later real acceptance.
- Source edits produce DRAFT and remove approval; revocation is terminal. Quote → case → source → Contact → Organization fences, original authorization/FLS, audit/outbox and rollback remain in place.
- Tests use a new isolated synthetic Workspace via official Development provisioning. No real email/provider calls, demo reset/reseed, default grants, commits or merges.
