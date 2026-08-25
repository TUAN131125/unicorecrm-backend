IF OBJECT_ID(N'[iam].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'iam') IS NULL EXEC(N'CREATE SCHEMA [iam];');
    CREATE TABLE [iam].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [iam].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823101906_InitialIdentityAuth'
)
BEGIN
    IF SCHEMA_ID(N'iam') IS NULL EXEC(N'CREATE SCHEMA [iam];');
END;

IF NOT EXISTS (
    SELECT * FROM [iam].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823101906_InitialIdentityAuth'
)
BEGIN
    CREATE TABLE [iam].[Accounts] (
        [AccountId] nvarchar(64) NOT NULL,
        [MemberId] nvarchar(64) NOT NULL,
        [Email] nvarchar(254) NOT NULL,
        [NormalizedEmail] nvarchar(254) NOT NULL,
        [DisplayName] nvarchar(160) NOT NULL,
        [Status] nvarchar(32) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [EmailVerifiedAt] datetimeoffset NULL,
        CONSTRAINT [PK_Accounts] PRIMARY KEY ([AccountId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [iam].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823101906_InitialIdentityAuth'
)
BEGIN
    CREATE TABLE [iam].[AuditRecords] (
        [Id] bigint NOT NULL IDENTITY,
        [Operation] nvarchar(96) NOT NULL,
        [Outcome] nvarchar(64) NOT NULL,
        [AccountId] nvarchar(64) NULL,
        [CorrelationId] nvarchar(128) NOT NULL,
        [OccurredAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_AuditRecords] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [iam].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823101906_InitialIdentityAuth'
)
BEGIN
    CREATE TABLE [iam].[IdempotencyRecords] (
        [Id] bigint NOT NULL IDENTITY,
        [Operation] nvarchar(96) NOT NULL,
        [Key] nvarchar(128) NOT NULL,
        [Fingerprint] nvarchar(64) NOT NULL,
        [ResourceId] nvarchar(64) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_IdempotencyRecords] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [iam].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823101906_InitialIdentityAuth'
)
BEGIN
    CREATE TABLE [iam].[SecurityEvents] (
        [EventId] nvarchar(64) NOT NULL,
        [EventType] nvarchar(96) NOT NULL,
        [AccountId] nvarchar(64) NULL,
        [CorrelationId] nvarchar(128) NOT NULL,
        [OccurredAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_SecurityEvents] PRIMARY KEY ([EventId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [iam].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823101906_InitialIdentityAuth'
)
BEGIN
    CREATE TABLE [iam].[Credentials] (
        [AccountId] nvarchar(64) NOT NULL,
        [PasswordHash] nvarchar(1024) NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_Credentials] PRIMARY KEY ([AccountId]),
        CONSTRAINT [FK_Credentials_Accounts_AccountId] FOREIGN KEY ([AccountId]) REFERENCES [iam].[Accounts] ([AccountId]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [iam].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823101906_InitialIdentityAuth'
)
BEGIN
    CREATE TABLE [iam].[Sessions] (
        [SessionId] nvarchar(64) NOT NULL,
        [AccountId] nvarchar(64) NOT NULL,
        [RefreshTokenHash] nvarchar(64) NOT NULL,
        [RefreshCounter] int NOT NULL,
        [Status] nvarchar(32) NOT NULL,
        [IssuedAt] datetimeoffset NOT NULL,
        [LastSeenAt] datetimeoffset NOT NULL,
        [IdleExpiresAt] datetimeoffset NOT NULL,
        [AbsoluteExpiresAt] datetimeoffset NOT NULL,
        [RevokedAt] datetimeoffset NULL,
        [RevokeReason] nvarchar(500) NULL,
        [DeviceId] nvarchar(64) NOT NULL,
        [DeviceLabel] nvarchar(160) NOT NULL,
        [UserAgent] nvarchar(512) NULL,
        CONSTRAINT [PK_Sessions] PRIMARY KEY ([SessionId]),
        CONSTRAINT [FK_Sessions_Accounts_AccountId] FOREIGN KEY ([AccountId]) REFERENCES [iam].[Accounts] ([AccountId]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [iam].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823101906_InitialIdentityAuth'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Accounts_MemberId] ON [iam].[Accounts] ([MemberId]);
END;

IF NOT EXISTS (
    SELECT * FROM [iam].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823101906_InitialIdentityAuth'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Accounts_NormalizedEmail] ON [iam].[Accounts] ([NormalizedEmail]);
END;

IF NOT EXISTS (
    SELECT * FROM [iam].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823101906_InitialIdentityAuth'
)
BEGIN
    CREATE UNIQUE INDEX [IX_IdempotencyRecords_Operation_Key] ON [iam].[IdempotencyRecords] ([Operation], [Key]);
END;

IF NOT EXISTS (
    SELECT * FROM [iam].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823101906_InitialIdentityAuth'
)
BEGIN
    CREATE INDEX [IX_Sessions_AccountId] ON [iam].[Sessions] ([AccountId]);
END;

IF NOT EXISTS (
    SELECT * FROM [iam].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823101906_InitialIdentityAuth'
)
BEGIN
    INSERT INTO [iam].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260823101906_InitialIdentityAuth', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [iam].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825013815_IdentityEmailVerification'
)
BEGIN
    CREATE TABLE [iam].[EmailVerificationChallenges] (
        [ChallengeId] nvarchar(64) NOT NULL,
        [AccountId] nvarchar(64) NOT NULL,
        [CodeHash] nvarchar(64) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [ExpiresAt] datetimeoffset NOT NULL,
        [ResendAvailableAt] datetimeoffset NOT NULL,
        [AttemptCount] int NOT NULL,
        [MaxAttempts] int NOT NULL,
        [ConsumedAt] datetimeoffset NULL,
        [SupersededAt] datetimeoffset NULL,
        CONSTRAINT [PK_EmailVerificationChallenges] PRIMARY KEY ([ChallengeId]),
        CONSTRAINT [FK_EmailVerificationChallenges_Accounts_AccountId] FOREIGN KEY ([AccountId]) REFERENCES [iam].[Accounts] ([AccountId]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [iam].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825013815_IdentityEmailVerification'
)
BEGIN
    CREATE INDEX [IX_EmailVerificationChallenges_AccountId_ConsumedAt_SupersededAt] ON [iam].[EmailVerificationChallenges] ([AccountId], [ConsumedAt], [SupersededAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [iam].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825013815_IdentityEmailVerification'
)
BEGIN
    INSERT INTO [iam].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260825013815_IdentityEmailVerification', N'10.0.11');
END;

COMMIT;
GO

