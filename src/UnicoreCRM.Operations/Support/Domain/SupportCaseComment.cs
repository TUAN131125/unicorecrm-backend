namespace UnicoreCRM.Operations.Support.Domain;

/// <summary>
/// Immutable Support conversation evidence. No admitted operation edits or deletes a reply
/// or an internal note, so the record is append-only. <c>IsInternal</c> keeps the internal
/// note semantically separated from the customer/agent-visible reply.
/// </summary>
internal sealed class SupportCaseComment
{
    private SupportCaseComment() { }

    internal SupportCaseComment(
        string workspaceId,
        string caseId,
        SupportCaseCommentType type,
        string body,
        string authorId,
        DateTimeOffset now)
    {
        CommentId = SupportIds.New("comment");
        WorkspaceId = workspaceId;
        CaseId = caseId;
        Type = type;
        Body = body;
        AuthorId = authorId;
        IsInternal = type == SupportCaseCommentType.InternalNote;
        CreatedAt = now;
    }

    public string CommentId { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public string CaseId { get; private set; } = null!;
    public SupportCaseCommentType Type { get; private set; }
    public string Body { get; private set; } = null!;
    public string AuthorId { get; private set; } = null!;
    public bool IsInternal { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
