IF OBJECT_ID(N'[products].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'products') IS NULL EXEC(N'CREATE SCHEMA [products];');
    CREATE TABLE [products].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [products].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826031143_ProductsCore'
)
BEGIN
    IF SCHEMA_ID(N'products') IS NULL EXEC(N'CREATE SCHEMA [products];');
END;

IF NOT EXISTS (
    SELECT * FROM [products].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826031143_ProductsCore'
)
BEGIN
    CREATE TABLE [products].[AuditRecords] (
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
    SELECT * FROM [products].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826031143_ProductsCore'
)
BEGIN
    CREATE TABLE [products].[IdempotencyRecords] (
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
    SELECT * FROM [products].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826031143_ProductsCore'
)
BEGIN
    CREATE TABLE [products].[OutboxMessages] (
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
    SELECT * FROM [products].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826031143_ProductsCore'
)
BEGIN
    CREATE TABLE [products].[Products] (
        [ProductId] nvarchar(128) NOT NULL,
        [WorkspaceId] nvarchar(128) NOT NULL,
        [Profile] nvarchar(max) NOT NULL,
        [NormalizedSku] nvarchar(80) NOT NULL,
        [ArchivedAt] datetimeoffset NULL,
        [ArchiveReason] nvarchar(1000) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        [Version] bigint NOT NULL,
        CONSTRAINT [PK_Products] PRIMARY KEY ([ProductId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [products].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826031143_ProductsCore'
)
BEGIN
    CREATE INDEX [IX_AuditRecords_AggregateId_OccurredAt] ON [products].[AuditRecords] ([AggregateId], [OccurredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [products].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826031143_ProductsCore'
)
BEGIN
    CREATE INDEX [IX_AuditRecords_WorkspaceId_OccurredAt] ON [products].[AuditRecords] ([WorkspaceId], [OccurredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [products].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826031143_ProductsCore'
)
BEGIN
    CREATE INDEX [IX_IdempotencyRecords_WorkspaceId_CreatedAt] ON [products].[IdempotencyRecords] ([WorkspaceId], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [products].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826031143_ProductsCore'
)
BEGIN
    CREATE INDEX [IX_OutboxMessages_WorkspaceId_OccurredAt] ON [products].[OutboxMessages] ([WorkspaceId], [OccurredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [products].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826031143_ProductsCore'
)
BEGIN
    CREATE INDEX [IX_Products_WorkspaceId_CreatedAt_ProductId] ON [products].[Products] ([WorkspaceId], [CreatedAt], [ProductId]);
END;

IF NOT EXISTS (
    SELECT * FROM [products].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826031143_ProductsCore'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Products_WorkspaceId_NormalizedSku] ON [products].[Products] ([WorkspaceId], [NormalizedSku]);
END;

IF NOT EXISTS (
    SELECT * FROM [products].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826031143_ProductsCore'
)
BEGIN
    INSERT INTO [products].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260826031143_ProductsCore', N'10.0.11');
END;

COMMIT;
GO

