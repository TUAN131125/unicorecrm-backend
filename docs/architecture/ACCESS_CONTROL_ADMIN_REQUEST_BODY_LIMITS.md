# AccessControl Administrative Request-Body Limits

## Authority

`PROJECT_EXTENSION_ACCESS_CONTROL_ADMIN_REQUEST_BODY_LIMITS`

PLAT-SEC-02 adds one transport-security amendment to the existing AccessControl administration
contracts. The adopted OpenAPI does not list HTTP 413 for these operations; this extension
supersedes only that omission. It changes no success request or response shape, business
validation, authorization capability, ownership, concurrency, idempotency, audit, event, or role
semantics.

## Protected operations

| Operation | Method and path |
|---|---|
| `createAccessRole` | `POST /access/roles` |
| `replaceAccessRole` | `PUT /access/roles/{roleId}` |
| `archiveAccessRole` | `POST /access/roles/{roleId}/archive` |
| `replaceWorkspaceMemberAccess` | `POST /access/members/{membershipId}/access` |

Each operation accepts at most **65,536 raw request-body bytes**. The bound is measured before text
decoding, so UTF-8 multibyte input receives no larger allowance. A declared `Content-Length` above
the limit is classified without reading the body. When length is absent, including chunked
transfer, the application reads no more than 65,537 bytes: the extra byte detects overflow and is
never passed to JSON normalization.

## Failure contract and precedence

An authorized request whose body exceeds the limit returns HTTP **413** with
`application/problem+json` and stable code `PAYLOAD_TOO_LARGE`. The response includes the existing
safe correlation identifier and contains no request body, target, version, role, membership,
credential, or persistence detail.

The existing security precedence remains authoritative:

1. authentication;
2. Trusted Workspace resolution;
3. AccessControl application-boundary evaluation of `access.configure`;
4. required request metadata and, where applicable, strong quoted `If-Match` validation;
5. request-body size result;
6. existing JSON normalization and business validation;
7. existing idempotency, target, version, lifecycle, transaction, governance-audit, and outbox
   behavior.

The HTTP boundary may read only the bounded transport envelope before the handler runs; it never
turns that read into a size or shape response. The application handler performs its existing single
`access.configure` evaluation before it observes the over-limit marker or validates JSON. A Trusted
Workspace caller without `access.configure` therefore receives the same `403 ACCESS_DENIED` for a
small malformed body and for an oversized body; request size reveals no target or validation fact,
and authorization is not moved earlier than its existing application boundary.

## Explicit non-changes

This extension does not change role lifecycle, capability assignment, membership-role assignment,
data-scope policy, field-security policy, `If-Match`, idempotency, transaction, governance audit, or
outbox semantics. It does not admit TEAM or CUSTOM evaluation semantics and does not introduce a
MASKED rendering representation. Existing unresolved semantics continue to fail closed.

## Verification

`scripts/verify-access-control-administrative-body-limits.ps1` starts the real ApiHost against an
isolated LocalDB database and verifies all four routes. It covers a normal authorized request,
exactly 65,536 bytes, 65,537 bytes, chunked overflow, UTF-8 multibyte overflow, malformed bodies,
metadata precedence, capability denial for both small and oversized bodies, one authorization
decision per request, stable problem details, and zero AccessControl mutation effects for rejected
requests. The existing operation-specific verification suites remain the regression authority for
the full success, concurrency, idempotency, governance-audit, and outbox behavior of each command.
