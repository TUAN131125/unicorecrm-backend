IF OBJECT_ID(N'[support].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'support') IS NULL EXEC(N'CREATE SCHEMA [support];');
    CREATE TABLE [support].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [support].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826083334_SupportCore'
)
BEGIN
    IF SCHEMA_ID(N'support') IS NULL EXEC(N'CREATE SCHEMA [support];');
END;

IF NOT EXISTS (
    SELECT * FROM [support].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826083334_SupportCore'
)
BEGIN
    CREATE TABLE [support].[AuditRecords] (
        [AuditId] nvarchar(128) NOT NULL,
        [Operation] nvarchar(96) NOT NULL,
        [WorkspaceId] nvarchar(128) NOT NULL,
        [ActorId] nvarchar(128) NOT NULL,
        [AggregateId] nvarchar(128) NULL,
        [RequestId] nvarchar(128) NOT NULL,
        [CorrelationId] nvarchar(128) NOT NULL,
        [Outcome] nvarchar(32) NOT NULL,
        [PriorVersion] bigint NULL,
        [NewVersion] bigint NULL,
        [OccurredAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_AuditRecords] PRIMARY KEY ([AuditId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [support].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826083334_SupportCore'
)
BEGIN
    CREATE TABLE [support].[IdempotencyRecords] (
        [ScopeKey] nvarchar(64) NOT NULL,
        [WorkspaceId] nvarchar(128) NOT NULL,
        [Operation] nvarchar(96) NOT NULL,
        [ActorId] nvarchar(128) NOT NULL,
        [TargetId] nvarchar(128) NOT NULL,
        [IdempotencyKey] nvarchar(128) NOT NULL,
        [Fingerprint] nvarchar(64) NOT NULL,
        [ResponseJson] nvarchar(max) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_IdempotencyRecords] PRIMARY KEY ([ScopeKey])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [support].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826083334_SupportCore'
)
BEGIN
    CREATE TABLE [support].[OutboxMessages] (
        [EventId] nvarchar(128) NOT NULL,
        [EventType] nvarchar(100) NOT NULL,
        [AggregateId] nvarchar(128) NOT NULL,
        [WorkspaceId] nvarchar(128) NOT NULL,
        [CorrelationId] nvarchar(128) NOT NULL,
        [PayloadJson] nvarchar(max) NOT NULL,
        [OccurredAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_OutboxMessages] PRIMARY KEY ([EventId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [support].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826083334_SupportCore'
)
BEGIN
    CREATE TABLE [support].[SupportCaseComments] (
        [CommentId] nvarchar(128) NOT NULL,
        [WorkspaceId] nvarchar(128) NOT NULL,
        [CaseId] nvarchar(128) NOT NULL,
        [Type] int NOT NULL,
        [Body] nvarchar(max) NOT NULL,
        [AuthorId] nvarchar(128) NOT NULL,
        [IsInternal] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_SupportCaseComments] PRIMARY KEY ([CommentId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [support].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826083334_SupportCore'
)
BEGIN
    CREATE TABLE [support].[SupportCases] (
        [CaseId] nvarchar(128) NOT NULL,
        [WorkspaceId] nvarchar(128) NOT NULL,
        [CaseNumber] nvarchar(128) NOT NULL,
        [CaseYear] int NOT NULL,
        [CaseSequence] int NOT NULL,
        [Title] nvarchar(300) NOT NULL,
        [Description] nvarchar(max) NOT NULL,
        [Status] int NOT NULL,
        [Priority] int NOT NULL,
        [Category] int NOT NULL,
        [Source] int NOT NULL,
        [Channel] int NULL,
        [RelationshipType] nvarchar(32) NOT NULL,
        [RelationshipId] nvarchar(128) NOT NULL,
        [ContactId] nvarchar(128) NULL,
        [RelatedOrderId] nvarchar(128) NULL,
        [RelatedProductId] nvarchar(128) NULL,
        [RelatedOwnedProductId] nvarchar(128) NULL,
        [OwnerId] nvarchar(128) NULL,
        [Tags] nvarchar(max) NOT NULL,
        [NextFollowUpAt] datetimeoffset NULL,
        [FirstResponseDueAt] datetimeoffset NULL,
        [ResolutionDueAt] datetimeoffset NULL,
        [ResolvedAt] datetimeoffset NULL,
        [ClosedAt] datetimeoffset NULL,
        [ReopenedAt] datetimeoffset NULL,
        [ResolutionSummary] nvarchar(4000) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        [Version] bigint NOT NULL,
        CONSTRAINT [PK_SupportCases] PRIMARY KEY ([CaseId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [support].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826083334_SupportCore'
)
BEGIN
    CREATE INDEX [IX_AuditRecords_AggregateId_OccurredAt] ON [support].[AuditRecords] ([AggregateId], [OccurredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [support].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826083334_SupportCore'
)
BEGIN
    CREATE INDEX [IX_AuditRecords_WorkspaceId_OccurredAt] ON [support].[AuditRecords] ([WorkspaceId], [OccurredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [support].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826083334_SupportCore'
)
BEGIN
    CREATE INDEX [IX_IdempotencyRecords_WorkspaceId_CreatedAt] ON [support].[IdempotencyRecords] ([WorkspaceId], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [support].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826083334_SupportCore'
)
BEGIN
    CREATE INDEX [IX_OutboxMessages_WorkspaceId_OccurredAt] ON [support].[OutboxMessages] ([WorkspaceId], [OccurredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [support].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826083334_SupportCore'
)
BEGIN
    CREATE INDEX [IX_SupportCaseComments_WorkspaceId_CaseId_CreatedAt] ON [support].[SupportCaseComments] ([WorkspaceId], [CaseId], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [support].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826083334_SupportCore'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SupportCases_WorkspaceId_CaseNumber] ON [support].[SupportCases] ([WorkspaceId], [CaseNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [support].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826083334_SupportCore'
)
BEGIN
    CREATE INDEX [IX_SupportCases_WorkspaceId_CaseYear_CaseSequence] ON [support].[SupportCases] ([WorkspaceId], [CaseYear], [CaseSequence]);
END;

IF NOT EXISTS (
    SELECT * FROM [support].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826083334_SupportCore'
)
BEGIN
    CREATE INDEX [IX_SupportCases_WorkspaceId_Status_CaseId] ON [support].[SupportCases] ([WorkspaceId], [Status], [CaseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [support].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826083334_SupportCore'
)
BEGIN
    CREATE INDEX [IX_SupportCases_WorkspaceId_UpdatedAt_CaseId] ON [support].[SupportCases] ([WorkspaceId], [UpdatedAt], [CaseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [support].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826083334_SupportCore'
)
BEGIN
    INSERT INTO [support].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260826083334_SupportCore', N'10.0.11');
END;

COMMIT;
GO

