namespace UnicoreCRM.Operations.Tasks.Contracts;

public enum ContactTaskReferenceStatus { Available, Archived, AccessDenied, NotFound }

/// <summary>
/// Contacts owns the current read/scope and lifecycle decision for a Task reference. The
/// Workspace is Tasks' trusted context; no Contact content is returned. Archived is distinct
/// internally so an authorized exact replay need not create a new association.
/// </summary>
public interface IContactTaskReferenceAuthority
{
    Task<ContactTaskReferenceStatus> ValidateAsync(string workspaceId, string contactId,
        string requestId, string correlationId, CancellationToken cancellationToken);
}
