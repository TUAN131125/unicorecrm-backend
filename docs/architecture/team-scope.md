# TEAM scope: ownership and migration boundary

Status: architecture decision supplied by the user on 2026-10-21. P0 retains fail-closed TEAM. This document is the prerequisite module matrix for P1; no TEAM migration or management API has been implemented by the Activity security change.

Workspace owns Teams and TeamMemberships in SQL Server. A Team belongs to exactly one Workspace. Membership requires an active Workspace member. Changes go through authorized backend operations with audit. AccessControl evaluates these facts and does not store membership.

A TEAM decision requires the module capability, valid Workspace membership, a record in that Workspace, an explicit valid record TeamId, active membership in that Team, field security and the applicable lifecycle checks. Missing TeamId denies TEAM. Role, ownerId, frontend state and free-form team names cannot supply a Team assignment. TEAM never falls back to OWN or WORKSPACE.

## Module matrix before P1

| Owner | Current authoritative record scope fact | Needs explicit TeamId for the requested CRM journey | Existing-data treatment | Assignment boundary |
|---|---|---|---|---|
| Workspace | Active Workspace membership; directory currently returns empty TeamIds | Owns Team and TeamMembership; no CRM record ownership | Create separate tables; do not derive membership from roles or display strings | Authorized Team and membership management, validation, audit and SQL constraints |
| Leads | `Lead.Profile.OwnerId` / synchronized `ScopeOwnerId` | Yes, when Lead TEAM is enabled | Add nullable module-owned TeamId; leave old rows null. `LeadProfile.AssignedTeam` remains display data and is not automatically migrated | Leads validates Workspace-owned TeamId before assignment; current record/write/field authority still applies |
| Contacts | `Contact.OwnerId` | Yes, for Sales TEAM Contact and its Activity | Nullable TeamId; old rows remain inaccessible under TEAM until explicitly assigned | Contacts command boundary validates Team and trusted Workspace |
| Organizations | `Organization.OwnerId` | Yes, for Organization TEAM and multi-reference Activity | Nullable TeamId; no inference from representatives or Contact affiliations | Organizations command boundary validates Team |
| Customers | `Customer.OwnerId` | Yes, for Customer TEAM and related work | Nullable TeamId; do not infer from Contact/Organization/Lead owner | Customers owns assignment. Conversion must explicitly define target Team handling before propagating TeamId |
| Tasks / task-based appointments | `TaskItem.AssigneeId` | Yes, if Task TEAM is enabled | Nullable TeamId; do not infer from assignee or linked Customer | Tasks validates Team independently of active assignee validation |
| Activity | Current capabilities, owner facts, lifecycle and relationships of every referenced CRM record | No independent Activity TeamId | Keep stored references; re-evaluate each owner on every request and replay | Referenced owners and AccessControl; no Activity actor-based ownership |
| History | Source-owned events and record references | No independent timeline TeamId | Preserve source identities; authorize each source before count/paging/projection | Read aggregation inherits all relevant record and field decisions |
| Quotes, Orders, Payments, Invoices and other modules | Existing owner facts | Outside this CRM journey change | No automatic schema change | Separate admitted requirements if TEAM is later required |

## P1 admission and compatibility requirements

1. Implement Workspace Team/TeamMembership storage, SQL migration, authorized management API, active-member and Workspace validation, immutable command audit, idempotency/concurrency and negative HTTP/SQL tests.
2. Publish a small Workspace-owned read/validation contract. Wire actual membership into the directory; stop rejecting nonempty membership lists only after the Workspace command boundary exists.
3. Extend the one AccessControl evaluator and its record facts/scope filters. Push module-owned TeamId predicates into list SQL before count and paging. Deny null, invalid, inactive and foreign-Workspace TeamIds.
4. Enable only modules in this matrix that have both validated assignment commands and compatible query predicates. Unconverted modules continue to deny TEAM.
5. Existing rows start with null TeamId. Reassignment must be an explicit authorized, auditable operation with concurrency checks. No bulk backfill from `assignedTeam`, owner or frontend data.
6. Define conversion target-Team semantics explicitly before enabling TEAM for the whole Lead-to-Customer workflow. Preserve old request fingerprints and valid retries when extending contracts.
7. Update OpenAPI and generate clients when adding Team contracts. Add same-Team positive tests and outside-Team, inactive-membership, cross-Workspace, unassigned-record, field-security and revoked-membership replay negative tests.

P0 evidence may prove TEAM denial only. Successful TEAM business operations remain BLOCKED until these requirements are implemented and tested.
