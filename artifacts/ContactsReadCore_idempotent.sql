IF OBJECT_ID(N'[contacts].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'contacts') IS NULL EXEC(N'CREATE SCHEMA [contacts];');
    CREATE TABLE [contacts].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [contacts].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260827163357_ContactsReadCore'
)
BEGIN
    IF SCHEMA_ID(N'contacts') IS NULL EXEC(N'CREATE SCHEMA [contacts];');
END;

IF NOT EXISTS (
    SELECT * FROM [contacts].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260827163357_ContactsReadCore'
)
BEGIN
    CREATE TABLE [contacts].[Contacts] (
        [ContactId] nvarchar(128) NOT NULL,
        [WorkspaceId] nvarchar(128) NOT NULL,
        [OwnerId] nvarchar(128) NULL,
        [FullName] nvarchar(200) NOT NULL,
        [Status] nvarchar(40) NOT NULL,
        [Version] bigint NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        [Profile] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_Contacts] PRIMARY KEY ([ContactId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [contacts].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260827163357_ContactsReadCore'
)
BEGIN
    CREATE INDEX [IX_Contacts_WorkspaceId_CreatedAt_ContactId] ON [contacts].[Contacts] ([WorkspaceId], [CreatedAt], [ContactId]);
END;

IF NOT EXISTS (
    SELECT * FROM [contacts].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260827163357_ContactsReadCore'
)
BEGIN
    CREATE INDEX [IX_Contacts_WorkspaceId_OwnerId_CreatedAt_ContactId] ON [contacts].[Contacts] ([WorkspaceId], [OwnerId], [CreatedAt], [ContactId]);
END;

IF NOT EXISTS (
    SELECT * FROM [contacts].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260827163357_ContactsReadCore'
)
BEGIN
    INSERT INTO [contacts].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260827163357_ContactsReadCore', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [contacts].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260827165741_ContactsReadAudit'
)
BEGIN
    CREATE TABLE [contacts].[ReadAuditRecords] (
        [AuditId] nvarchar(128) NOT NULL,
        [Operation] nvarchar(128) NOT NULL,
        [WorkspaceId] nvarchar(128) NOT NULL,
        [ActorId] nvarchar(128) NOT NULL,
        [ContactId] nvarchar(128) NULL,
        [RequestId] nvarchar(128) NOT NULL,
        [CorrelationId] nvarchar(128) NOT NULL,
        [ContactVersion] bigint NULL,
        [OccurredAt] datetimeoffset(7) NOT NULL,
        CONSTRAINT [PK_ReadAuditRecords] PRIMARY KEY ([AuditId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [contacts].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260827165741_ContactsReadAudit'
)
BEGIN
    CREATE INDEX [IX_ReadAuditRecords_ContactId_OccurredAt] ON [contacts].[ReadAuditRecords] ([ContactId], [OccurredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [contacts].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260827165741_ContactsReadAudit'
)
BEGIN
    CREATE INDEX [IX_ReadAuditRecords_WorkspaceId_OccurredAt] ON [contacts].[ReadAuditRecords] ([WorkspaceId], [OccurredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [contacts].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260827165741_ContactsReadAudit'
)
BEGIN
    INSERT INTO [contacts].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260827165741_ContactsReadAudit', N'10.0.11');
END;

COMMIT;
GO

