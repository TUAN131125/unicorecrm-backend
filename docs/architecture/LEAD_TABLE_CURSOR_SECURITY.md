# Lead Table context-bound cursor

Decision: resolve `LC-CA-001` using the existing ASP.NET Core DataProtection provider,
with Table purpose `Leads.ListCursor.v1` and a canonical effective-context subpurpose.
The former Base64 keyset authenticated neither its issuer nor its query context.
Authenticated Workspace B requests could consume Workspace A tokens with HTTP 200;
SQL workspace predicates still applied, so that observation did not prove disclosure.

Binding includes trusted WorkspaceId, parsed work state, validated owner/assignment
filters, trimmed invariant-uppercase search (empty becomes null), effective record
scope and OWN owner, unassigned-read authority, phone readability and the existing
AccessControl policy fingerprint. Cursor input never supplies trusted authority.
JSON property order and enum serialization are fixed by this version's binding code;
changes to that format must deliberately consider token compatibility.

There is no additional individual-principal restriction. Equal effective authority
in one workspace can continue the same Table query; OWN scope binds its resolved
member owner. Extra principal isolation remains a product/security decision. The
fingerprint hashes effective capabilities/scopes/FLS, not a role revision counter.
Equivalent policy remains equivalent after version-only edits; unrelated effective
policy changes may conservatively invalidate continuation, following existing Kanban
precedent. No owner authority is widened by accepting an equivalent-context token.

Page size is excluded: the continuation still selects strictly after the last
`UpdatedAt DESC, LeadId DESC` key. Changing the window size preserves ordering and
uniqueness; the connected proof exercises 50/25/45 across 120 rows. Request IDs,
correlation IDs and time are excluded. No new expiry policy is introduced.

Null cursor starts a query. Empty, oversized (over 4096 characters), malformed,
tampered, wrong-purpose, authenticated invalid-keyset and legacy unprotected cursors
fail closed. Expected cryptographic/format/argument failures become the existing
422 `VALIDATION_FAILED` cursor field error before List/Count. There is no legacy
fallback or implicit HTTP-200 restart. Clients recover by explicitly resetting the
query and requesting a fresh first page.

The shared inner keyset codec remains unchanged because Kanban uses it. Persistence
predicates, ordering, exact authorized count, audit behavior, FLS projection, public
request/response schemas and DI registrations are unchanged. No migration or schema
change is needed. Kanban and Contact protection purposes remain independent.

Verification and repeatable commands are in
[LeadTableCursorVerifier](../../scripts/LeadTableCursorVerifier/README.md). Tests cover
rejection before persistence, real authenticated HTTP/SQL boundaries, normal and
equal-timestamp pagination, filtered counts, adjacent cursor purposes, and connected
browser first/next/query-reset/error recovery. Unit tests alone are not the admission
evidence. The baseline red run explicitly reproduced foreign-workspace HTTP 200.

Deployment limitation: ApplicationName remains `UnicoreCRM.AI`. Shared production
key-ring topology is **NOT_DECIDED**; multi-replica cursor acceptance is
**NOT_ESTABLISHED**. Replicas need compatible application name, purposes, key ring and
decryption access. This change neither configures production keys nor proves that
deployment. Existing unprotected client tokens are intentionally invalidated on
deployment; users must reset their query to receive protected continuations.
