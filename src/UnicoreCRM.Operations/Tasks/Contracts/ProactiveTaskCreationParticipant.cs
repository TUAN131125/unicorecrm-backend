namespace UnicoreCRM.Operations.Tasks.Contracts;

public interface IProactiveTaskCreationParticipant
{
    Task<ProactiveTaskCreationResult> CreateAsync(ProactiveTaskCreationCommand command, CancellationToken cancellationToken);
}

public sealed record ProactiveTaskCreationCommand(string ProactiveItemId, string? Title, string? Description,
    string? AssigneeId, string? DueAt, string? Priority, string IdempotencyKey, string RequestId, string CorrelationId);

public sealed record ProactiveTaskCreationError(string Code, int Status, string Title,
    IReadOnlyDictionary<string, string[]>? FieldErrors);

public sealed record ProactiveTaskCreationResult(string? TaskId, long? TaskVersion, string? Outcome,
    ProactiveTaskCreationError? Error = null);
