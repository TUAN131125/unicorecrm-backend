using UnicoreCRM.Operations.Tasks.Application.Common;
using UnicoreCRM.Operations.Tasks.Contracts;

namespace UnicoreCRM.Operations.Tasks.Application.CreateProactiveFollowUp;

internal sealed class Participant(CreateTask.Handler createTask) : IProactiveTaskCreationParticipant
{
    public async Task<ProactiveTaskCreationResult> CreateAsync(ProactiveTaskCreationCommand command, CancellationToken cancellationToken)
    {
        var request = new CreateTaskRequest(command.Title, command.AssigneeId, command.DueAt,
            command.Description, command.Priority, SourceRef: new("PROACTIVE_AI", command.ProactiveItemId));
        var result = await createTask.HandleAsync(new(request,
            new TaskCommandMetadata(command.RequestId, command.CorrelationId, command.IdempotencyKey, null)), cancellationToken);
        if (!result.IsSuccess)
        {
            var error = result.Error!;
            return new(null, null, null, new(error.Code, error.Status, error.Title, error.FieldErrors));
        }
        var response = result.Value!;
        return new(response.AggregateId, response.Version, response.Outcome);
    }
}
