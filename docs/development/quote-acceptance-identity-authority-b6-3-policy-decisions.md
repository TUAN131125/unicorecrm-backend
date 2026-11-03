# B6.3 — Identity & Signer Authority: approved policy and boundary

26/10/2026. **PACKAGE A APPROVED WITH USER AMENDMENTS.** Narrow implementation and Development verification are recorded in the acceptance report. No actual source/method is admitted. Three capabilities are explicitly provisioned only in the new synthetic Workspace; no default grants or identity/source verification are claimed.

## 1. Accepted invariants — not reopened

User B6.3 task and prior Option B decisions authorize case-bound documentary evidence work. Keep VND GrandTotal ≤100,000,000 for the initial workflow, other currencies only under explicit Workspace policy, no FX, authority freshness ≤24 hours before acceptance. These are workflow limits, not a determination of legal authority.

CLEAN, assessed identity/authority, independent VERIFIED review and Quote ACCEPTED remain distinct. B6.3 never issues a verified acceptance/commercial commitment receipt or unblocks MarkWon. No B6.4/B6.5/Gate C, portal/OTP/KYC provider, real customer contact or default permission grants.

## 2. AnalysisRecord — fresh local evidence

- Backend HEAD `13cd39eba01ebc9dc238f8b0ef98e174c1d4d3f6`, frontend HEAD `814cc4685b6d8f8ead2fc945f4607a2d0168320d`; both dirty. Fresh hashes cover2,914 Backend and2,710 frontend existing files; the previously deleted frontend file remains deleted.
- Verified exact local SQL Development:275 Quotes,195 Deals,38 B5 issued documents,28 acceptance proofs,73 Workspaces. Snapshot now covers35 tables, including B6.2 jobs/attempts/results. This was captured before task writes. Subsequent fixtures are isolated and enumerated in the acceptance report.
- Fresh Backend baseline339PASS/4FAIL/343; global TypeScript retains the same3 diagnostics. [Entry artifacts](../../../task-runtime/quote-identity-authority-b6-3/task-packet.json).
- `ContactOrganizationRelationship` allows several active Organizations per Contact and one primary active affiliation per Contact. Role/title/primary are CRM assertions; relationship changes bump parent Contact.Version. Open-ended CRM affiliation does not imply open-ended signing authority.
- `Customer.RelationshipType/RelationshipId` identifies a commercial account subject, distinct from stakeholder links. Quote BuyerRef remains CONTACT or ORGANIZATION_ACCOUNT; CustomerId is not a substitute Buyer identity. Both direct-sale and Deal-linked Quotes use this Buyer model.
- `IContactLinkReader` exposes label/eligibility only. Existing B5 Contact/Organization readers provide authorized versioned snapshots under owner leases. The internal organization-contact ID reader lacks the relationship details/FLS/lease needed for this decision.
- No implemented/admitted customer identity source registry, signer delegation verifier or actual source adapter was found in bounded local source review. IAM account/email verification does not establish customer identity or signing authority.
- Existing Workspace seller configuration and Integrations connection settings have different owners/purposes. They must not be relabeled as an identity-source trust registry.

## 3. Recommended package A — useful partial implementation, positive source trust blocked

Implement claims/bindings, a source-descriptor registry, negative source checks and guarded assessments using actual API/SQL/browser. Keep approved source/method allowlist empty until a concrete source and verification method receive approval. Do not insert a fake operational source or let SourceName/checkboxes produce SOURCE_CHECKED.

Full positive verification of an actual source remains **BLOCKED_CONTRACT**. Synthetic facts can test rules but cannot prove an actual representative was checked. Report PARTIAL_FUNCTIONAL_PASS where evidence binding, source-unavailable handling and persistence work.

### Owner and case binding

- Quotes owns case-specific identity subjects, authority claims, source descriptors/check attempts and assessment history. Reuse QuotesDbContext/audit/outbox/idempotency; no global signing registry or duplicate proof bytes.
- Reuse the existing Acceptance Case. Buyer/root/revision/DocumentId/commercial fingerprint/issued PDF hash are derived from the Case and B5 owner, never chosen from client authority flags.
- Add one narrow party-capture lease in Quotes.Contracts, implemented by the existing CRM owners: exact BuyerRef + candidate ContactId + explicit affiliation RelationshipId; return only authorized eligibility/display/version/relationship facts, observed time and binding digest. CRM does not return IdentityVerified/AuthorityVerified.
- Enforce Contact/Organization record scope and `organizationRelationships` FLS. Preserve multiple affiliations; do not select primary/first contact as signer. Contact and relationship version changes invalidate current assessment eligibility; immutable historical snapshots remain.
- Initial binding supports the exact CONTACT Buyer or an explicit active Contact–Organization affiliation for an ORGANIZATION_ACCOUNT Buyer. Other claimed delegation must remain insufficient until a real admitted authority source establishes it.

### Evidence and authority claims

- Identity references: existing IDENTITY/SUPPORTING_DOCUMENT Proofs; authority references: DELEGATION/SUPPORTING_DOCUMENT. Exact same Workspace/Case/Quote/document, stored SHA and current effective CLEAN are required. A combined document may serve both purposes without copying bytes.
- Names are captured from the authorized Contact owner. No structured government-ID number, date of birth, credentials, biometrics or blanket KYC collection. Proof content remains exclusively in B6.1 storage.
- Record submitted issuer/source references, claimed effective interval, exact Quote applicability and claimed currency/amount restrictions as **claims**, not checked source facts. Missing limitations never mean unlimited authority. Unknown product/business restriction support blocks readiness.
- Client trust/status/verifier/checkedAt flags are not accepted. Backend supplies actor/time/status. No SOURCE_CHECKED result is constructed from staff-entered facts.
- Explicit subject/binding changes create a new version with reason and retained history; no silent retargeting of Buyer, subject, ProofId or SHA. No new Case merely to replace a wrong candidate.

### Source registry and checking

- Source descriptors are Workspace/Buyer-scoped, versioned and initially DRAFT/unapproved; contain source type, issuer reference, verification method key, requirements and provenance references. Purpose is fixed by this contract to Quote acceptance; no operational source validity or legal authority is inferred from a draft. Actual source validity/admission requires a later concrete source adapter decision. Admin revocation appends a version/event with reason.
- Registering a descriptor does not approve its trust. Activation requires an explicitly admitted Backend method plus an implemented actual source adapter. With no method/source available, activation/check returns a clear blocked result; no approved operational source is prepopulated.
- A source-check command records an attempted check and truthful SOURCE_UNAVAILABLE/INSUFFICIENT outcome. `AttemptedAt` is server time; `CheckedAt` remains absent unless an actual admitted check succeeded. It cannot mint a VerificationReceipt.
- Source method trust criteria, issuer origin validation, revocation discovery and privileged approval procedure must be approved for the concrete source before positive SOURCE_CHECKED can be enabled. This package does not select a registry/provider or treat CRM data as its replacement.

### Authorization proposal

Reuse `quotes.read` + `quotes.readAcceptanceEvidence` for metadata/history and `quotes.submitAcceptanceEvidence` for submitted bindings. Preserve all Quote/Case/source-owner record/FLS checks.

Add separately assignable, no-default capabilities:

- `quotes.manageAcceptanceEvidenceSources`: draft/version/revoke source descriptors and future admitted source administration; never grants file access.
- `quotes.recordAcceptanceSourceCheck`: request/record checks under an admitted method; also requires existing read/evidence and `quotes.readAcceptanceProofContent` for documentary source checking.
- `quotes.assessAcceptanceIdentityAuthority`: run bounded metadata/fact assessment; no independent verification or acceptance authority and no raw PDF read.

Read-only assessment does not need file permission merely to inspect already-authorized metadata. Any proof download/documentary viewing/checking still requires the dedicated B6.2 content capability and effective CLEAN. Source checker is not implicitly the B6.4 verifier. Neither Workspace Owner status nor quotes.update overrides these boundaries.

### Approved narrow HTTP surface

- Source registry list/register/version/revoke under `/quotes/{quoteId}/acceptance-evidence-sources` (explicit Quote context ensures Buyer, record scope and source-document FLS authorization).
- Read/update versioned bindings, request source check, record rule assessment and page history under `/quotes/{quoteId}/acceptance-cases/{caseId}/identity-authority`.
- Extend existing acceptance readiness with this component; do not create a second acceptance decision engine. Existing root verification status remains NOT_VERIFIED/BLOCKED while downstream gates/sources are absent.
- All mutations use strong If-Match on their own case-bound identity/source aggregate, stable actor/Workspace-scoped idempotency, current authorization on replay and atomic SQL/audit/outbox. They do not bump Quote resource version or mutate lifecycle.
- Immutable version/check/assessment history, scoped composite FKs/indexes, bounded50-item reads, bounded references/text and cancellation. External source calls must occur outside SQL transactions; current no-adapter branch makes no external calls.

### Privacy/purpose

Purpose is limited to documentary evidence for this Quote acceptance Case. Reuse existing encrypted proof controls and technical immutable commercial-history retention; no global identity dossier or new raw-document store. Technical indefinite retention is not a legal retention/compliance assertion. Jurisdiction, lawful collection basis and production retention/deletion obligations require deployment-specific review before using real identity material; tests use synthetic business data only.

## 4. Alternative B — concrete approved source available now

Provide only the source/method description and authorization scope: issuer, what independent facts can be checked, origin authentication, Buyer coverage, authority scope/limits, effective dates/revocation, provenance retrieval and permitted access. Do not provide customer documents, passwords or API keys in chat.

Review and admit that exact source/method before implementing an actual positive adapter. An organization directory entry, ContactId, email or job title alone is not adequate. No third-party customer requests are made without explicit workflow authorization.

## 5. Authority boundary

The current task authorizes B6.3 analysis and safe independent work. `.unicore-ai/manifests/implementation-freedom.json` marks new public contracts/cross-owner dependencies PROPOSAL_ONLY and authorization/privacy semantics LOCKED. The backend builder skill requires an authority decision where new boundary semantics remain unresolved. This concrete package is presented to admit the narrow changes together; prior100-million/24-hour decisions are not being asked again.

User approved package A and its three separate capabilities/narrow CRM lease/case-bound immutable history. Implementation may proceed within that scope. Concrete positive identity/authority source admission remains BLOCKED_CONTRACT; do not activate SOURCE_CHECKED/Ready, B6.4/B6.5/Gate C.
