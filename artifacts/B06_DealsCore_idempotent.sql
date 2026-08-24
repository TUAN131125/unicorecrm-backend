IF OBJECT_ID(N'[deals].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'deals') IS NULL EXEC(N'CREATE SCHEMA [deals];');
    CREATE TABLE [deals].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [deals].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823140929_B06_DealsCore'
)
BEGIN
    IF SCHEMA_ID(N'deals') IS NULL EXEC(N'CREATE SCHEMA [deals];');
END;

IF NOT EXISTS (
    SELECT * FROM [deals].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823140929_B06_DealsCore'
)
BEGIN
    CREATE TABLE [deals].[AuditRecords] (
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
    SELECT * FROM [deals].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823140929_B06_DealsCore'
)
BEGIN
    CREATE TABLE [deals].[Deals] (
        [DealId] nvarchar(128) NOT NULL,
        [WorkspaceId] nvarchar(128) NOT NULL,
        [Profile] nvarchar(max) NOT NULL,
        [StageCode] nvarchar(120) NOT NULL,
        [StageCategory] nvarchar(16) NOT NULL,
        [ForecastCategory] nvarchar(24) NOT NULL,
        [ForecastHistory] nvarchar(max) NOT NULL,
        [StageEnteredAt] datetimeoffset NOT NULL,
        [NextActionAt] datetimeoffset NULL,
        [NextActionSummary] nvarchar(1000) NULL,
        [NextActionType] nvarchar(16) NULL,
        [NextActionId] nvarchar(128) NULL,
        [WinEvidenceType] nvarchar(32) NULL,
        [WinEvidenceSourceId] nvarchar(128) NULL,
        [WinEvidenceOccurredAt] datetimeoffset NULL,
        [WonAt] datetimeoffset NULL,
        [LostAt] datetimeoffset NULL,
        [ActualCloseDate] date NULL,
        [LostReason] nvarchar(500) NULL,
        [LostReasonNote] nvarchar(2000) NULL,
        [RecycleDecision] nvarchar(32) NULL,
        [RecycleEligible] bit NULL,
        [RevisitAt] datetimeoffset NULL,
        [ArchivedAt] datetimeoffset NULL,
        [ArchiveReason] nvarchar(500) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        [Version] bigint NOT NULL,
        CONSTRAINT [PK_Deals] PRIMARY KEY ([DealId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [deals].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823140929_B06_DealsCore'
)
BEGIN
    CREATE TABLE [deals].[IdempotencyRecords] (
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
    SELECT * FROM [deals].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823140929_B06_DealsCore'
)
BEGIN
    CREATE TABLE [deals].[OutboxMessages] (
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
    SELECT * FROM [deals].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823140929_B06_DealsCore'
)
BEGIN
    CREATE INDEX [IX_AuditRecords_AggregateId_OccurredAt] ON [deals].[AuditRecords] ([AggregateId], [OccurredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [deals].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823140929_B06_DealsCore'
)
BEGIN
    CREATE INDEX [IX_AuditRecords_WorkspaceId_OccurredAt] ON [deals].[AuditRecords] ([WorkspaceId], [OccurredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [deals].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823140929_B06_DealsCore'
)
BEGIN
    CREATE INDEX [IX_Deals_WorkspaceId_StageCategory_StageCode] ON [deals].[Deals] ([WorkspaceId], [StageCategory], [StageCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [deals].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823140929_B06_DealsCore'
)
BEGIN
    CREATE INDEX [IX_Deals_WorkspaceId_UpdatedAt_DealId] ON [deals].[Deals] ([WorkspaceId], [UpdatedAt], [DealId]);
END;

IF NOT EXISTS (
    SELECT * FROM [deals].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823140929_B06_DealsCore'
)
BEGIN
    CREATE INDEX [IX_IdempotencyRecords_WorkspaceId_CreatedAt] ON [deals].[IdempotencyRecords] ([WorkspaceId], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [deals].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823140929_B06_DealsCore'
)
BEGIN
    CREATE INDEX [IX_OutboxMessages_WorkspaceId_OccurredAt] ON [deals].[OutboxMessages] ([WorkspaceId], [OccurredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [deals].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823140929_B06_DealsCore'
)
BEGIN
    INSERT INTO [deals].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260823140929_B06_DealsCore', N'10.0.11');
END;

COMMIT;
GO

