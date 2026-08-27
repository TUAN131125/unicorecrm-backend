using UnicoreCRM.Operations.Tasks.Application.Common;
using UnicoreCRM.Operations.Tasks.Contracts;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Operations.Tasks.Application.ProvideTaskRecordAccessFacts;

/// <summary>
/// The Tasks side of the narrow record-access fact boundary. Tasks stays authoritative for TaskItem
/// existence, for the member reference record scope is defined against, and for its own capability
/// and command vocabulary. AccessControl stays authoritative for the decision.
///
/// <para>It authorizes nothing - AccessControl authorizes the caller before calling it - performs one
/// read-only lookup already scoped to the trusted Workspace, so a task belonging to another
/// Workspace is reported as not found, and writes no Task state, audit record or outbox message.</para>
/// </summary>
internal sealed class TaskRecordAccessFactProvider(ITasksPersistence persistence) : IRecordAccessFactProvider
{
    /// <summary>
    /// Only capabilities behind an admitted Tasks operation are declared. Tasks has no delete,
    /// export or approval operation, so those stay null and the matching actions are denied rather
    /// than being granted a capability name nothing enforces. The frontend also asks about
    /// `task.reopen` and `task.delete`; neither has an admitted operation, so neither is declared
    /// and neither can ever be granted.
    /// </summary>
    private static readonly RecordAccessResourceDescriptor TasksDescriptor = RecordAccessResourceDescriptor.Create(
        resourceKey: TaskAuthorization.ResourceKey,
        readCapability: TaskCapabilities.Read.Capability,
        updateCapability: TaskCapabilities.Update.Capability,
        commandCapabilities: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["task.create"] = TaskCapabilities.Create.Capability,
            ["task.update"] = TaskCapabilities.Update.Capability,
            ["task.assign"] = TaskCapabilities.Assign.Capability,
            ["task.complete"] = TaskCapabilities.Complete.Capability
        },
        enforceableFields: TaskFieldSecurity.EnforceableFields);

    public RecordAccessResourceDescriptor Descriptor => TasksDescriptor;

    public async Task<RecordAccessFacts> ReadFactsAsync(
        TrustedWorkspaceContext trustedWorkspace,
        string recordId,
        RecordAccessRequestContext requestContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trustedWorkspace);
        if (!TaskValidation.IsEntityId(recordId))
            return RecordAccessFacts.NotFound;

        var task = await persistence.ReadTaskAsync(trustedWorkspace.WorkspaceId, recordId, cancellationToken);
        // The assignee is the only member reference the Task aggregate records. Nothing else -
        // actor, author or source - is substituted for a record owner.
        return task is null ? RecordAccessFacts.NotFound : RecordAccessFacts.Found(task.AssigneeId);
    }
}
