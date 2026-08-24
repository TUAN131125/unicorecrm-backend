using UnicoreCRM.Workflows.Atomic.Application.Common;
using UnicoreCRM.Workflows.Atomic.Contracts;

namespace UnicoreCRM.Workflows.Atomic.Application.ProvisionInitialWorkspace;

internal sealed record Command(
    string AccountId,
    string MemberId,
    ProvisionInitialWorkspaceRequest Request,
    AtomicWorkflowMetadata Metadata);
