namespace UnicoreCRM.Operations.Support.Domain;

/// <summary>
/// The Support case lifecycle admitted by current Support authority. The canonical design
/// baseline (design-authority/canonical-design/modules/support.md) and the verified OpenAPI
/// <c>SupportCaseStatus</c> schema declare exactly these eight states.
/// </summary>
internal enum SupportCaseStatus
{
    New = 0,
    InProgress = 1,
    WaitingCustomer = 2,
    WaitingInternal = 3,
    Resolved = 4,
    Closed = 5,
    Reopened = 6,
    Cancelled = 7
}

/// <summary>Ordinals ascend with severity so the admitted <c>priority</c> sort is meaningful.</summary>
internal enum SupportCasePriority
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}

/// <summary>
/// The full read/replace category set. Creation is restricted to the first seven values by
/// the OpenAPI <c>SupportCaseCreateCategory</c> schema; the remaining five stay readable and
/// replaceable because <c>SupportCaseCategory</c> declares them.
/// </summary>
internal enum SupportCaseCategory
{
    Request = 0,
    Consultation = 1,
    Complaint = 2,
    FollowUp = 3,
    Onboarding = 4,
    UsageIssue = 5,
    PostPurchase = 6,
    TechnicalSupport = 7,
    Warranty = 8,
    CustomerCare = 9,
    Billing = 10,
    FeatureRequest = 11
}

internal enum SupportCaseSource
{
    Manual = 0,
    Customer360 = 1,
    Email = 2,
    Phone = 3,
    Chat = 4,
    WebForm = 5,
    Order = 6,
    Product = 7
}

internal enum SupportCaseChannel
{
    Email = 0,
    Phone = 1,
    Chat = 2,
    Meeting = 3,
    Internal = 4
}

/// <summary>
/// Support-owned conversation evidence kinds. <c>AgentReply</c> is the only reply kind an
/// admitted Support command can produce: every admitted command runs under an authenticated
/// Workspace member holding <c>support.update</c>. No admitted operation ingests a customer
/// reply, so <c>CustomerReply</c> stays reserved and unreachable.
/// </summary>
internal enum SupportCaseCommentType
{
    CustomerReply = 0,
    AgentReply = 1,
    InternalNote = 2
}
