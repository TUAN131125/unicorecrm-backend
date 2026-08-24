using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using UnicoreCRM.Operations.Tasks.Application.Common;
using UnicoreCRM.Operations.Tasks.Domain;
using Domain = UnicoreCRM.Operations.Tasks.Domain;

namespace UnicoreCRM.Operations.Tasks.Infrastructure.Persistence;

internal sealed class EfTasksPersistence(TasksDbContext dbContext) : ITasksPersistence
{
    public async Task<ITasksTransaction> BeginSerializableAsync(CancellationToken cancellationToken) =>
        new TasksTransaction(await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken));

    public Task<TaskItem?> LoadTaskAsync(string workspaceId, string taskId, CancellationToken cancellationToken) =>
        dbContext.Tasks.SingleOrDefaultAsync(item => item.WorkspaceId == workspaceId && item.TaskId == taskId, cancellationToken);

    public Task<TaskItem?> ReadTaskAsync(string workspaceId, string taskId, CancellationToken cancellationToken) =>
        dbContext.Tasks.AsNoTracking().SingleOrDefaultAsync(item => item.WorkspaceId == workspaceId && item.TaskId == taskId, cancellationToken);

    public async Task<TasksPage<TaskItem>> ListTasksAsync(
        string workspaceId,
        TaskListSpecification specification,
        CancellationToken cancellationToken)
    {
        IQueryable<TaskItem> query = dbContext.Tasks.AsNoTracking().Where(item => item.WorkspaceId == workspaceId);
        if (!string.IsNullOrEmpty(specification.Search))
            query = query.Where(item => item.Title.Contains(specification.Search) || (item.Description != null && item.Description.Contains(specification.Search)) || (item.RecordLabel != null && item.RecordLabel.Contains(specification.Search)) || item.AssigneeId.Contains(specification.Search));
        if (specification.Status is not null)
            query = query.Where(item => item.Status == specification.Status);
        if (specification.Priority is not null)
            query = query.Where(item => item.Priority == specification.Priority);
        if (specification.AssigneeId is not null)
            query = query.Where(item => item.AssigneeId == specification.AssigneeId);
        if (specification.RelationshipType is not null)
            query = query.Where(item => item.RelationshipType == specification.RelationshipType);
        if (specification.RelationshipId is not null)
            query = query.Where(item => item.RelationshipId == specification.RelationshipId);
        if (specification.RecordModuleKey is not null)
            query = query.Where(item => item.RecordModuleKey == specification.RecordModuleKey);
        if (specification.RecordId is not null)
            query = query.Where(item => item.RecordId == specification.RecordId);
        if (specification.OverdueAt is not null)
            query = query.Where(item => item.Status == Domain.TaskStatus.Open && item.DueAt < specification.OverdueAt);

        var totalCount = await query.CountAsync(cancellationToken);
        query = OrderTasks(query, specification.SortBy, specification.Descending);
        var items = await query.Skip(specification.Offset).Take(specification.Limit + 1).ToListAsync(cancellationToken);
        var hasNext = items.Count > specification.Limit;
        if (hasNext)
            items.RemoveAt(items.Count - 1);
        return new TasksPage<TaskItem>(items, hasNext, hasNext ? specification.Offset + specification.Limit : null, totalCount);
    }

    public async Task<TasksPage<TaskActivity>> ListActivitiesAsync(
        string workspaceId,
        ActivityListSpecification specification,
        CancellationToken cancellationToken)
    {
        IQueryable<TaskActivity> query = dbContext.Activities.AsNoTracking().Where(item => item.WorkspaceId == workspaceId);
        if (!string.IsNullOrEmpty(specification.Search))
            query = query.Where(item => item.Subject.Contains(specification.Search) || (item.Body != null && item.Body.Contains(specification.Search)));
        if (specification.Type is not null)
            query = query.Where(item => item.Type == specification.Type);
        if (specification.ActorId is not null)
            query = query.Where(item => item.ActorId == specification.ActorId);
        if (specification.RelationshipType is not null)
            query = query.Where(item => item.RelationshipType == specification.RelationshipType);
        if (specification.RelationshipId is not null)
            query = query.Where(item => item.RelationshipId == specification.RelationshipId);
        if (specification.RecordModuleKey is not null)
            query = query.Where(item => item.RecordModuleKey == specification.RecordModuleKey);
        if (specification.RecordId is not null)
            query = query.Where(item => item.RecordId == specification.RecordId);
        if (specification.OccurredFrom is not null)
            query = query.Where(item => item.OccurredAt >= specification.OccurredFrom);
        if (specification.OccurredTo is not null)
            query = query.Where(item => item.OccurredAt <= specification.OccurredTo);

        var totalCount = await query.CountAsync(cancellationToken);
        query = specification.Descending
            ? query.OrderByDescending(item => item.OccurredAt).ThenByDescending(item => item.ActivityId)
            : query.OrderBy(item => item.OccurredAt).ThenBy(item => item.ActivityId);
        var items = await query.Skip(specification.Offset).Take(specification.Limit + 1).ToListAsync(cancellationToken);
        var hasNext = items.Count > specification.Limit;
        if (hasNext)
            items.RemoveAt(items.Count - 1);
        return new TasksPage<TaskActivity>(items, hasNext, hasNext ? specification.Offset + specification.Limit : null, totalCount);
    }

    public Task<TaskIdempotencyRecord?> FindIdempotencyAsync(string scopeKey, CancellationToken cancellationToken) =>
        dbContext.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(item => item.ScopeKey == scopeKey, cancellationToken);

    public void AddTask(TaskItem task) => dbContext.Tasks.Add(task);
    public void AddActivity(TaskActivity activity) => dbContext.Activities.Add(activity);
    public void AddIdempotency(TaskIdempotencyRecord record) => dbContext.IdempotencyRecords.Add(record);
    public void AddAudit(TaskAuditRecord audit) => dbContext.AuditRecords.Add(audit);
    public void AddOutbox(TaskOutboxMessage message) => dbContext.OutboxMessages.Add(message);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new TasksPersistenceConcurrencyException(exception);
        }
    }

    private static IQueryable<TaskItem> OrderTasks(IQueryable<TaskItem> query, string sortBy, bool descending) =>
        (sortBy, descending) switch
        {
            ("createdAt", true) => query.OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.TaskId),
            ("createdAt", false) => query.OrderBy(item => item.CreatedAt).ThenBy(item => item.TaskId),
            ("dueAt", true) => query.OrderByDescending(item => item.DueAt).ThenByDescending(item => item.TaskId),
            ("dueAt", false) => query.OrderBy(item => item.DueAt).ThenBy(item => item.TaskId),
            ("priority", true) => query.OrderByDescending(item => item.Priority).ThenByDescending(item => item.TaskId),
            ("priority", false) => query.OrderBy(item => item.Priority).ThenBy(item => item.TaskId),
            ("title", true) => query.OrderByDescending(item => item.Title).ThenByDescending(item => item.TaskId),
            ("title", false) => query.OrderBy(item => item.Title).ThenBy(item => item.TaskId),
            ("updatedAt", false) => query.OrderBy(item => item.UpdatedAt).ThenBy(item => item.TaskId),
            _ => query.OrderByDescending(item => item.UpdatedAt).ThenByDescending(item => item.TaskId)
        };

    private sealed class TasksTransaction(IDbContextTransaction transaction) : ITasksTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
