# Lead module — independent reviewer packet

Task: `LEAD-NURTURE-FREEZE-10B` · Operation: `qualifyLeadForNurture` · Owner: `leads` · Risk: `CRITICAL`
Revision: Task 10C (governance gap and gate admission closure)

This packet contains references only. It carries **no PASS claim**: prior tasks' results are excluded
on purpose, because a prior claim is not evidence. Every finding below must be re-verified
independently.

## 1. Subjects

| Subject | Identity |
|---|---|
| Backend implementation | commit `ff5047f997854935ce7fe2271dcc893ca6cce987` on branch `lead-module/freeze-subject`, tree `f81f8b9e7684cbc51954a2456a2bbbebb1f78170` |
| Base of the reviewed diff | commit `d3b1c117ef5c03d6ae12d8d214ba86a03797ac6e` (`git diff d3b1c11 ff5047f`, 69 files) |
| Diff identity | `diffSha256` `ce85eae24e32a626b13d61ec3513c9cc89e1f30152c6b05d3d6c383e17ecf1c7` |
| Task packet | `backend-work/tasks/LEAD-NURTURE-FREEZE-10B.json`, sha256 `3d1fc2a2a9e7ff4171d341863212148b67595b11d16102d9eea8563cc8319d28` |
| Analysis record | `backend-work/handoffs/LEAD-NURTURE-FREEZE-10B/analysis.json`, sha256 `cdab3cb373de934f265218f3b99213b189fb36c36ff7e7f9021b89cf4b911a03` |
| Architecture record | `backend-work/handoffs/LEAD-NURTURE-FREEZE-10B/architecture.json`, sha256 `0406a8380ffc16166bbf026bd093ee89202508f73d46ebe7bb2cc7057d2f5596` |
| Controlling wire contract | `frontend/unicorecrm-web/docs/api/openapi.json` sha256 `d98462853a5c529ce1695978d35541a8bc000dc25b2781a62fd8bf5e91cd6a57` (**uncommitted**; frontend HEAD `c12a182f4df86976b018b09d2d9080d0ab46b722` carries `fd079b2f…`) |
| Contract artifact manifest | `authorityEvidence[CONTRACT-LIVE-ARTIFACTS].value` — 8 files by sha256 |
| Authority pinset | `authorityEvidence[AUTHORITY-PINSET].value` — 13 files by sha256 |

The backend commit SHA alone does **not** identify the public wire contract.

**Two contract identities are carried deliberately.** `identity.baseline.openApiSha256` is
`8278547d…`, the design-authority provenance copy. `supersession-ledger.json` (entry
`DEC-LEAD-CONTACT-NAME-BOUND`) states that file "stays unedited as dated
`PINNED_FRONTEND_WIRE_EVIDENCE`" while the live contract was regenerated to `d98462…`. The
controlling contract for behaviour is `d98462…`; the baseline field is the pinned provenance
reference. See `authorityEvidence[CONTRACT-PROVENANCE-SUPERSESSION]`.

## 2. Migration set

```
20260903020308_LeadQualificationAnchor              (workflow)
20260903020311_LeadQualificationRelationship        (leads)
20260903023638_LeadQualificationAnchorResponse      (workflow)
20260903070156_ContactQualificationResultReceipt    (contacts)
20260903070156_NurtureRecoveryFacts                 (workflow)
```

Model snapshots touched: Contacts, Leads, Workflows.

## 3. Findings to re-check independently

| Id | Defect |
|---|---|
| F1 | Replay authorization — authorize before any anchor disclosure |
| F2 | Field-write security — every effective Lead field write guarded |
| F3 | Workflow fingerprint — Task owner intent inside identity, `ExpectedVersion` outside it |
| F4 | Refreshed `If-Match` recovery on the same Idempotency-Key |
| F5 | Durable response convergence across a lost participant acknowledgment |
| F6 | Complete request-contract validation before the first owner mutation |
| G1 | Contact canonical name bound (`displayName` 1–200) |
| FN-1 | NURTURE reason preserved in full; Task title is a bounded derived summary |
| FN-2 | Transient Contacts contention is not a permanent relationship-invalid 422 |

## 4. Governance validator commands

Run from the workspace root. `--design-authority-root design-authority` is **required**: without it
`verify_operation` cannot locate the authority from `--repo-root backend` and reports
`AUTHORITY_UNVERIFIED`.

```
python .unicore-ai/tools/validate_task.py        backend/backend-work/tasks/LEAD-NURTURE-FREEZE-10B.json --repo-root backend
python .unicore-ai/tools/check_baseline_drift.py backend/backend-work/tasks/LEAD-NURTURE-FREEZE-10B.json --repo-root backend --design-authority-root design-authority
python .unicore-ai/tools/validate_diff_scope.py  backend/backend-work/tasks/LEAD-NURTURE-FREEZE-10B.json --repo-root backend
python .unicore-ai/tools/validate_evidence.py --repo-root backend --task backend/backend-work/tasks/LEAD-NURTURE-FREEZE-10B.json
python .unicore-ai/tools/run_gate.py --repo-root backend --task backend/backend-work/tasks/LEAD-NURTURE-FREEZE-10B.json --gate-id build --trust-level LOCAL --runner-id <id>
```

Current results: `validate_diff_scope` PASS · `check_baseline_drift` CURRENT_VERIFIED ·
`validate_task` FAIL (5 governance-owner items, section 6) · `validate_evidence --for-freeze` FAIL
(`FREEZE_REQUIRES_TRUSTED_EVIDENCE`).

## 5. Maintained verification commands

Run from `backend/`. Each takes an isolated `-DatabaseName` and drops it afterwards. These are
**diagnostic** runs: `tools/run_and_record_evidence.py` is disabled by design, so they are not
governance evidence until an adopted gate registry defines them as gates.

```
powershell -NoProfile -File scripts\verify-lead-nurture-qualification.ps1        -DatabaseName <db>
powershell -NoProfile -File scripts\verify-lead-nurture-qualification-api.ps1    -DatabaseName <db>
powershell -NoProfile -File scripts\verify-contact-qualification-participant.ps1 -DatabaseName <db>
powershell -NoProfile -File scripts\verify-access-control-record-access.ps1      -DatabaseName <db>   # includes verify-lead-lifecycle.ps1
powershell -NoProfile -File scripts\verify-lead-interested-products.ps1          -DatabaseName <db>
powershell -NoProfile -File scripts\verify-inbound-lead-webhook.ps1              -DatabaseName <db>
powershell -NoProfile -File scripts\verify-support-core.ps1                      -DatabaseName <db>   # createTask regression
dotnet build UnicoreCRM.slnx --no-restore
```

Contract gate, from `frontend/unicorecrm-web/`: `npm run api:check`.

**Operational note:** a finished harness can leave an `UnicoreCRM.ApiHost.exe` alive that holds a
driver's stdout handle and stalls the next suite with no output. If a run appears hung between
suites, check `Get-CimInstance Win32_Process -Filter "Name='UnicoreCRM.ApiHost.exe'"` and
`Stop-Process -Force`.

## 6. Governance items reserved to other principals

These block admission and are **not** implementation defects.

1. **Architecture gate** — `tests/UnicoreCRM.ArchitectureTests` does not exist and
   `wire_architecture_tests.py` generates a project referencing `UnicoreCRM.Domain/.Application/.Infrastructure/.Api`,
   none of which exist in this modular monolith.
2. **Contract gate** — `tests/UnicoreCRM.ContractTests` does not exist; the real mechanism is
   `npm run api:check` in the frontend repository.
3. **Subject rebind** — an adopted `backend/architecture/gate-registry.json` is the only supported
   way to fix 1 and 2, and that path is hard-denied to task diffs and cannot stay uncommitted, so it
   forces a rebase of this subject (`IMMUTABLE_SUBJECT_REBIND_REQUIRED`).
4. **Cross-owner edges** — `approvedCrossOwnerEdges` is empty; the four participant edges frozen by
   `lead-contact-qualification-authority.md` §10.2 were never transcribed.
5. **Persistence ownership** — `leads`, `contacts`, `tasks`, `products` are all
   `IMPLEMENTATION_MAPPING_REQUIRED` although each already has durable writes.
6. **Trusted evidence** — freeze-grade PASS needs `TRUSTED_CI` or `EXTERNAL_CONFORMANCE`; no control
   plane ref, CI workflow or attestation key exists.

## 7. Deployment prerequisites — carried forward, not fixed

1. **Legacy workflow anchors.** `IntentVersion != 1` anchors are refused with `INTERNAL_ERROR`
   rather than reconstructed. Existing rows must be reconciled before deployment.
2. **Legacy Contact conversion receipts.** Rows with a null `ResultJson` stay fail-closed; the
   original owner-returned name and version are never rebuilt from current mutable Contact rows.

## 8. Known non-blocking authority follow-up

`AUTHORITY_FOLLOW_UP` — exhausted internal contention terminates as the admitted `INTERNAL_ERROR` /
HTTP 500, correctly no longer a false relationship-invalid 422, but the operation's admitted taxonomy
has no correctly classified *retriable* internal-contention code: only `RATE_LIMITED` and
`INTEGRATION_UNAVAILABLE` are catalogued `retryable: true` and neither describes this condition. No
code was invented.

## 9. Documentation defect observed, deliberately not corrected

`src/UnicoreCRM.Workflows/Atomic/AtomicModule.cs` comments that the NURTURE coordinator "maps no
route"; `ApiHost/Program.cs` maps `MapLeadQualificationEndpoints()`. Left untouched so the subject
stays byte-identical to the reviewed source. Correct it in a separate task.

## 10. Governance state and role separation

State is `PROPOSED`. `PROPOSED → ANALYZED` was attempted with the canonical tool and correctly
refused with `ANALYSIS_HAS_UNRESOLVED_GAPS`: `analysis.json` records six `BLOCKING_GOVERNANCE_GAP`
entries, and emptying that list to force progression is forbidden.

`primaryWriter` = `claude-opus-5-implementer`. `reviewer` = `pending-independent-reviewer`
(`INDEPENDENT_REVIEWER_PENDING`). Gatekeeper unassigned. No role has been self-attested and no
`REVIEW_PASS` or `FROZEN` record exists.
