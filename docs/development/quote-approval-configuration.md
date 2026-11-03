# Workspace Quote approval configuration

B1 uses the Workspace-owned `IQuoteApprovalPolicyReader`. Configure each Workspace explicitly in normal Backend configuration, environment variables, or the existing ignored Development local configuration. No Studio redesign, browser policy import, global Workspace fallback or database seed is used.

The user's initial defaults, approved 2026-10-24:

```json
{
  "QuoteApprovalPolicies": {
    "Workspaces": {
      "<existing-workspace-id>": {
        "Version": "quote-approval-20261024-v1",
        "LineDiscountPercent": 10,
        "TotalDiscountPercent": 10,
        "PostpaidDays": 30,
        "AdvancedPaymentRequiresApproval": true,
        "AlwaysRequireApproval": false,
        "GrandTotalLimits": { "VND": 100000000 }
      }
    }
  }
}
```

Only `>` triggers a threshold. Missing currency limits require approval; there is no FX conversion. Full single remainder payment relative to Order/Delivery/Invoice using calendar days and nonnegative offsets is ordinary; advanced/custom terms require review. Explicit values can change per Workspace without changing application source. Bump Version on changes; a content hash also binds the effective version to values if a label is reused accidentally. Missing/invalid settings produce INTEGRATION_UNAVAILABLE with APPROVAL_POLICY_MISSING_OR_INVALID; reads are unaffected.

Request approval evaluates authoritative persisted totals/snapshots. Required approval is blocked with QUOTE_APPROVAL_REQUIRED / NO_ELIGIBLE_APPROVER unless an active different Workspace member has quotes.read + quotes.approve, record scope, commercial read FLS and approval write FLS. Actual approval independently rechecks the actor and rejects creator/requester even for Workspace Owners. No credentials or memberships are configured by this policy.

Creator evidence comes from exactly one immutable Quote Core creation command. Legacy or ambiguous records remain readable but cannot obtain approval. Commercial replacement of Draft/Review returns Draft, clears all approval bindings under FLS, and retains the prior binding in immutable command/outbox history. An old idempotency replay reports its original commitment; it does not authorize current content or delivery. Future delivery must freshly verify content, policy and current state.

The non-seeding verification harness applies this configuration to its existing synthetic Workspace only through child-process environment variables and restores them afterward. It does not silently configure other local or production Workspaces. A separate deployment configuration must explicitly supply their policies.
