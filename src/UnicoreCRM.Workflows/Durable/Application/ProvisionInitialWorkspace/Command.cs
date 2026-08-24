using UnicoreCRM.Workflows.Durable.Application.Common;
using UnicoreCRM.Workflows.Durable.Contracts;

namespace UnicoreCRM.Workflows.Durable.Application.ProvisionInitialWorkspace;

internal sealed record Command(
    string AccountId,
    string MemberId,
    ProvisionInitialWorkspaceRequest Request,
    DurableWorkflowMetadata Metadata);
